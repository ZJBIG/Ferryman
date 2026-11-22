using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ferryman.Characters;
using Ferryman.Combat;
using Ferryman.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = System.Random;

namespace Ferryman.Voyage
{
    public enum VoyagePhase { Departure, Entering, Sailing, Event, Island, Leaving, Transition, Success, Failure, Error }

    public sealed class VoyageController : MonoBehaviour
    {
        private VoyageDefinition definition;
        private VoyageSaveStore store;
        private VoyageVessel vessel;
        private Coroutine routine;
        private readonly List<Passenger> passengers = new List<Passenger>();
        private readonly List<EnemyCharacter> enemies = new List<EnemyCharacter>();
        private readonly List<DestructibleObstacle> eventObstacles = new List<DestructibleObstacle>();
        private PlannedVoyageEvent[] eventPlan;
        private int seed;
        private string runId;
        private RewardData pendingReward;
        private int deliveredPassengers;
        private int waveIndex;
        private bool settlementSuccess;

        public VoyageDefinition Definition => definition;
        public VoyageVessel Vessel => vessel;
        public VoyagePhase Phase { get; private set; } = VoyagePhase.Departure;
        public int StopIndex { get; private set; }
        public int WaveIndex => waveIndex;
        public bool ActiveRun { get; private set; }
        public string Message { get; private set; }
        public float Fade { get; private set; }
        public float SailingRemaining { get; private set; }
        public RewardData PendingReward => pendingReward;
        public int DeliveredPassengers => deliveredPassengers;
        public PermanentProgress Progress => store?.Progress;
        public bool HasCheckpoint => store != null && store.HasCheckpoint;
        public bool CanRetrySettlement { get; private set; }
        public IReadOnlyList<Passenger> Passengers => passengers;
        public IReadOnlyList<EnemyCharacter> Enemies => enemies;
        public IReadOnlyList<PlannedVoyageEvent> EventPlan => eventPlan;

        public void Initialize(VoyageDefinition voyage, VoyageSaveStore saves)
        {
            definition = voyage;
            store = saves;
            DontDestroyOnLoad(gameObject);
            ValidateDefinition();
            Message = "选择第一位护送乘客；启航前船只已完成整备。";
        }

        public void ReportError(Exception error)
        {
            if (routine != null)
                StopCoroutine(routine);
            routine = null;
            Phase = VoyagePhase.Error;
            Message = error.Message;
            Fade = 0f;
            if (vessel != null)
                vessel.SetActionsAllowed(false);
            CleanupHazards();
        }

        public bool StartVoyage(CrewDefinition initialPassenger, int voyageSeed)
        {
            if (ActiveRun || HasCheckpoint || initialPassenger == null || !definition.initialPassengers.Contains(initialPassenger))
                return false;
            seed = voyageSeed;
            runId = Guid.NewGuid().ToString("N");
            pendingReward = default;
            deliveredPassengers = 0;
            CanRetrySettlement = false;
            eventPlan = VoyageEventPlanner.Build(definition, seed);
            CreateVessel();
            BoardPassenger(initialPassenger, definition.stops.Length - 1);
            vessel.SetActionsAllowed(false, true);
            ActiveRun = true;
            StopIndex = 0;
            SaveCheckpoint();
            routine = StartCoroutine(RunSafely(RunRoute(0, false)));
            return true;
        }

        public bool ResumeVoyage()
        {
            if (ActiveRun || store == null)
                return false;
            VoyageCheckpoint checkpoint = store.LoadCheckpoint(definition.id);
            if (checkpoint == null)
                return false;
            ValidatePassengers(checkpoint);
            seed = checkpoint.seed;
            runId = checkpoint.runId;
            pendingReward = new RewardData { coins = checkpoint.pendingCoins, specialItems = checkpoint.pendingSpecialItems };
            deliveredPassengers = checkpoint.deliveredPassengers;
            eventPlan = VoyageEventPlanner.Build(definition, seed);
            CreateVessel();
            vessel.Boat.RestoreState(checkpoint.ship);
            RestoreActorPosition(vessel.Player, checkpoint.playerLocalPosition);
            foreach (PassengerState saved in checkpoint.passengers)
            {
                Passenger passenger = BoardPassenger(FindProfile(saved.profileId), saved.destinationStopIndex);
                RestoreActorPosition(passenger.Actor, saved.localPosition);
            }
            vessel.SetActionsAllowed(false, true);
            ActiveRun = true;
            Message = "已恢复区域检查点。";
            routine = StartCoroutine(RunSafely(RunRoute(checkpoint.stopIndex, true)));
            return true;
        }

        private IEnumerator RunSafely(IEnumerator operation)
        {
            var pending = new Stack<IEnumerator>();
            pending.Push(operation);
            while (pending.Count > 0)
            {
                object current = null;
                Exception failure = null;
                bool moved = false;
                try
                {
                    moved = pending.Peek().MoveNext();
                    if (moved)
                        current = pending.Peek().Current;
                }
                catch (Exception error) { failure = error; }
                if (failure != null)
                {
                    routine = null;
                    ReportError(failure);
                    yield break;
                }
                if (!moved)
                    pending.Pop();
                else if (current is IEnumerator nested)
                    pending.Push(nested);
                else
                    yield return current;
            }
            routine = null;
        }

        private IEnumerator RunRoute(int firstStop, bool alreadyArrived)
        {
            for (int index = firstStop; index < definition.stops.Length && ActiveRun; index++)
            {
                StopIndex = index;
                VoyageStop stop = definition.stops[index];
                vessel.SetActionsAllowed(false, true);
                Phase = VoyagePhase.Transition;
                Fade = 1f;
                if (!TryPresentStop(stop) && (index != firstStop || SceneManager.GetActiveScene().name != stop.sceneName))
                    yield return SceneManager.LoadSceneAsync(stop.sceneName);
                Phase = VoyagePhase.Entering;
                yield return AnimateEntry();
                if (!ActiveRun)
                    yield break;
                vessel.SetActionsAllowed(true);

                if (stop.kind == VoyageStopKind.Island)
                {
                    Phase = VoyagePhase.Island;
                    if (!alreadyArrived && index > 0)
                    {
                        DeliverPassengers(index);
                        yield return BoardAtIsland(stop, index);
                    }
                    Message = "抵达 " + stop.displayName;
                    SaveCheckpoint();
                    if (index == definition.stops.Length - 1)
                    {
                        FinishRun(true);
                        yield break;
                    }
                    yield return WaitActive(definition.transitionSeconds);
                }
                else
                {
                    SaveCheckpoint();
                    Phase = VoyagePhase.Sailing;
                    Message = "自动航行中";
                    yield return Sail(definition.sailingSeconds * 0.5f);
                    if (!ActiveRun)
                        yield break;
                    if (eventPlan[index].kind != VoyageEventKind.None)
                        yield return ExecuteEvent(eventPlan[index]);
                    if (!ActiveRun)
                        yield break;
                    Phase = VoyagePhase.Sailing;
                    Message = "继续航行";
                    yield return Sail(definition.sailingSeconds * 0.5f);
                }
                alreadyArrived = false;
                if (!ActiveRun)
                    yield break;
                CleanupHazards();
                vessel.SetActionsAllowed(false, true);
                Phase = VoyagePhase.Leaving;
                yield return AnimateExit();
            }
        }

        private IEnumerator AnimateEntry()
        {
            float duration = definition.transitionSeconds;
            float start = Time.time;
            while (Time.time - start < duration && ActiveRun)
            {
                float t = Mathf.Clamp01((Time.time - start) / duration);
                vessel.transform.position = Vector3.Lerp(new Vector3(-22f, 0f), Vector3.zero, Mathf.SmoothStep(0f, 1f, t));
                Fade = 1f - Mathf.Clamp01(t * 3f);
                yield return null;
            }
            vessel.transform.position = Vector3.zero;
            Fade = 0f;
        }

        private IEnumerator AnimateExit()
        {
            float duration = definition.transitionSeconds;
            float start = Time.time;
            while (Time.time - start < duration && ActiveRun)
            {
                float t = Mathf.Clamp01((Time.time - start) / duration);
                vessel.transform.position = Vector3.Lerp(Vector3.zero, new Vector3(22f, 0f), Mathf.SmoothStep(0f, 1f, t));
                Fade = Mathf.Clamp01((t - 0.5f) * 2f);
                yield return null;
            }
            Fade = 1f;
        }

        private IEnumerator Sail(float duration)
        {
            SailingRemaining = duration;
            while (SailingRemaining > 0f && ActiveRun)
            {
                SailingRemaining = Mathf.Max(0f, SailingRemaining - Time.deltaTime);
                yield return null;
            }
        }

        private IEnumerator WaitActive(float duration)
        {
            float until = Time.time + duration;
            while (Time.time < until && ActiveRun)
                yield return null;
        }

        private IEnumerator ExecuteEvent(PlannedVoyageEvent planned)
        {
            Phase = VoyagePhase.Event;
            if (planned.kind == VoyageEventKind.Combat)
            {
                EncounterDefinition encounter = planned.encounter;
                for (waveIndex = 0; waveIndex < encounter.waves.Length && ActiveRun; waveIndex++)
                {
                    Message = encounter.displayName + $"  第 {waveIndex + 1}/{encounter.waves.Length} 轮";
                    SpawnWave(encounter.waves[waveIndex]);
                    while (enemies.Any(enemy => enemy != null && enemy.Health > 0f) && ActiveRun)
                        yield return null;
                    enemies.Clear();
                }
                if (ActiveRun)
                    AddReward(encounter.reward);
            }
            else
            {
                SeaEventDefinition special = planned.specialEvent;
                Message = special.displayName + "：右键清除甲板上的物品";
                SpawnObstacles(special);
                while (eventObstacles.Any(obstacle => obstacle != null && !obstacle.IsCleared) && ActiveRun)
                    yield return null;
                eventObstacles.Clear();
                if (ActiveRun)
                    AddReward(special.reward);
            }
        }

        private void SpawnWave(WaveDefinition wave)
        {
            foreach (EnemySpawn group in wave.enemies)
            for (int unit = 0; unit < group.count; unit++)
            {
                Vector2 position = group.spawnPosition + Vector2.right * (unit - (group.count - 1) * 0.5f) * 0.8f;
                var enemy = Instantiate(group.prefab, position, Quaternion.identity);
                enemy.InitializeEnemy(vessel.Boat, vessel.Player, vessel.DamageSystem);
                enemies.Add(enemy);
            }
        }

        private void SpawnObstacles(SeaEventDefinition special)
        {
            BoatDeck deck = vessel.Boat.Deck;
            Vector2 footprint = special.obstaclePrefab.GetComponent<BoxCollider2D>().size;
            Vector2Int center = deck.WorldToCell(Vector2.zero);
            var cells = new List<Vector2Int>();
            for (int x = 0; x < deck.GridSize.x; x++)
            for (int y = 0; y < deck.GridSize.y; y++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                if (BoatDeck.CellDistance(center, cell) > 1)
                    cells.Add(cell);
            }
            var random = new Random(seed ^ StopIndex * 397);
            Physics2D.SyncTransforms();
            for (int i = 0; i < special.obstacleCount && cells.Count > 0; i++)
            {
                int selected = random.Next(cells.Count);
                Vector2 position = deck.CellToWorld(cells[selected]);
                cells.RemoveAt(selected);
                if (!deck.CanPlace(position, footprint))
                {
                    i--;
                    continue;
                }
                var item = Instantiate(special.obstaclePrefab, position, Quaternion.identity);
                var obstacle = item.GetComponent<DestructibleObstacle>();
                var bomb = item.GetComponent<TimedBomb>();
                if (bomb != null)
                    bomb.BindBoat(vessel.Boat);
                eventObstacles.Add(obstacle);
                Physics2D.SyncTransforms();
            }
            if (eventObstacles.Count != special.obstacleCount)
                throw new InvalidOperationException("甲板没有足够的事件物品放置空间。");
        }

        private Passenger BoardPassenger(CrewDefinition profile, int destination)
        {
            Vector2 position = FindBoardingPosition();
            Character actor = Instantiate(profile.prefab, position, Quaternion.identity, vessel.transform);
            var passenger = actor.GetComponent<Passenger>() ?? actor.gameObject.AddComponent<Passenger>();
            passenger.Initialize(profile, destination);
            if (actor is CombatCrewMember combat)
                combat.InitializeCrew(vessel.Boat, vessel.DamageSystem, vessel.Player.ProjectilePrefab, profile);
            else if (actor is MaintenanceCrewMember maintenance)
                maintenance.InitializeCrew(vessel.Boat, profile);
            passengers.Add(passenger);
            return passenger;
        }

        private Vector2 FindBoardingPosition()
        {
            BoatDeck deck = vessel.Boat.Deck;
            Physics2D.SyncTransforms();
            for (int y = deck.GridSize.y - 1; y >= 0; y--)
            for (int x = 0; x < deck.GridSize.x; x++)
            {
                Vector2 position = deck.CellToWorld(new Vector2Int(x, y));
                if (deck.CanPlace(position, new Vector2(0.5f, 0.75f)))
                    return position;
            }
            throw new InvalidOperationException("No boarding position is available on the deck.");
        }

        private void RestoreActorPosition(Character actor, Vector2 localPosition)
        {
            var hitbox = actor.GetComponent<BoxCollider2D>();
            Vector2 desired = vessel.transform.TransformPoint(localPosition);
            Physics2D.SyncTransforms();
            if (!vessel.Boat.Deck.TryFindPlacement(desired + hitbox.offset, hitbox.size, hitbox, out Vector2 center))
                throw new InvalidDataException("No clear deck position for a checkpoint actor.");
            actor.transform.position = center - hitbox.offset;
            var body = actor.GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.position = actor.transform.position;
                body.simulated = true;
            }
        }

        private IEnumerator BoardAtIsland(VoyageStop stop, int index)
        {
            var futureIslands = Enumerable.Range(index + 1, definition.stops.Length - index - 1)
                .Where(next => definition.stops[next].kind == VoyageStopKind.Island).ToArray();
            if (futureIslands.Length == 0 || definition.passengerPool.Length == 0 || stop.maxBoardingPassengers <= 0)
                yield break;
            var random = new Random(seed ^ index * 9973);
            if (random.NextDouble() >= stop.boardingChance)
                yield break;
            int count = random.Next(1, stop.maxBoardingPassengers + 1);
            for (int i = 0; i < count && ActiveRun; i++)
            {
                CrewDefinition profile = definition.passengerPool[random.Next(definition.passengerPool.Length)];
                int destination = futureIslands[random.Next(futureIslands.Length)];
                Message = profile.displayName + " 将上船 · " + DescribeRole(profile) + " · 目的地 " + definition.stops[destination].displayName;
                yield return WaitActive(definition.boardingNoticeSeconds);
                if (ActiveRun)
                    BoardPassenger(profile, destination);
            }
        }

        public static string DescribeRole(CrewDefinition profile)
        {
            return profile.role == CrewRole.Combat ? $"战斗岗 · 攻击 {profile.attackPower:0} / 间隔 {profile.fireInterval:0.0}s"
                : $"维护岗 · {profile.repairSpeed:0} HP/s · 范围 {profile.repairRange} · 同修 {profile.simultaneousRepairs}";
        }

        private void DeliverPassengers(int index)
        {
            for (int i = passengers.Count - 1; i >= 0; i--)
            {
                Passenger passenger = passengers[i];
                if (passenger.DestinationStopIndex != index)
                    continue;
                AddReward(new RewardData { coins = passenger.Definition.coinsReward, specialItems = passenger.Definition.specialItemsReward });
                deliveredPassengers++;
                passengers.RemoveAt(i);
                passenger.Disembark();
            }
        }

        private void AddReward(RewardData reward)
        {
            pendingReward.coins = checked(pendingReward.coins + reward.coins);
            pendingReward.specialItems = checked(pendingReward.specialItems + reward.specialItems);
        }

        private void CreateVessel()
        {
            CleanupVessel();
            vessel = Instantiate(definition.vesselPrefab).GetComponent<VoyageVessel>();
            DontDestroyOnLoad(vessel.gameObject);
            vessel.SetActionsAllowed(false, true);
            vessel.Boat.Destroyed.AddListener(OnBoatDestroyed);
        }

        private void SaveCheckpoint()
        {
            store.SaveCheckpoint(new VoyageCheckpoint
            {
                version = 1,
                voyageId = definition.id,
                runId = runId,
                seed = seed,
                stopIndex = StopIndex,
                pendingCoins = pendingReward.coins,
                pendingSpecialItems = pendingReward.specialItems,
                deliveredPassengers = deliveredPassengers,
                playerLocalPosition = vessel.Player.transform.localPosition,
                ship = vessel.Boat.CaptureState(),
                passengers = passengers.Select(passenger => new PassengerState
                {
                    profileId = passenger.Definition.id,
                    destinationStopIndex = passenger.DestinationStopIndex,
                    localPosition = passenger.transform.localPosition
                }).ToArray()
            });
        }

        private void OnBoatDestroyed()
        {
            if (!ActiveRun)
                return;
            if (routine != null)
                StopCoroutine(routine);
            FinishRun(false);
        }

        private void FinishRun(bool success)
        {
            vessel.SetActionsAllowed(false);
            CleanupHazards();
            settlementSuccess = success;
            CanRetrySettlement = true;
            try { store.Settle(runId, pendingReward); }
            catch (Exception error)
            {
                ReportError(error);
                return;
            }
            CanRetrySettlement = false;
            ActiveRun = false;
            Fade = 0f;
            Phase = success ? VoyagePhase.Success : VoyagePhase.Failure;
            Message = success ? "航程完成" : "船只被毁，本轮失败";
            routine = null;
        }

        public void RetrySettlement()
        {
            if (CanRetrySettlement)
                FinishRun(settlementSuccess);
        }

        public void AbandonVoyage()
        {
            if (routine != null)
                StopCoroutine(routine);
            ActiveRun = false;
            routine = null;
            store.AbandonCheckpoint();
            CanRetrySettlement = false;
            pendingReward = default;
            deliveredPassengers = 0;
            CleanupVessel();
            routine = StartCoroutine(RunSafely(ReturnToDeparture()));
        }

        public void ReturnHome()
        {
            if (ActiveRun)
                return;
            CleanupVessel();
            routine = StartCoroutine(RunSafely(ReturnToDeparture()));
        }

        public void QuitGame()
        {
            if (ActiveRun)
                store.AbandonCheckpoint();
            Application.Quit();
        }

        private IEnumerator ReturnToDeparture()
        {
            Phase = VoyagePhase.Transition;
            Fade = 1f;
            if (!TryPresentStop(definition.stops[0]))
                yield return SceneManager.LoadSceneAsync(definition.stops[0].sceneName);
            else
                yield return null;
            StopIndex = 0;
            Fade = 0f;
            Phase = VoyagePhase.Departure;
            Message = "选择第一位护送乘客；启航前船只已完成整备。";
        }

        private bool TryPresentStop(VoyageStop stop)
        {
            Camera camera = Camera.main;
            var presentation = camera != null ? camera.GetComponent<VoyageScenePresentation>() : null;
            if (presentation == null || !presentation.IsUnifiedScene)
                return false;
            presentation.PresentStop(stop);
            return true;
        }

        private void CleanupHazards()
        {
            foreach (var enemy in enemies)
                if (enemy != null)
                {
                    enemy.gameObject.SetActive(false);
                    Destroy(enemy.gameObject);
                }
            enemies.Clear();
            foreach (var shot in FindObjectsOfType<Projectile>())
            {
                shot.Hitbox.enabled = false;
                Destroy(shot.gameObject);
            }
            foreach (var bomb in FindObjectsOfType<TimedBomb>())
            {
                bomb.gameObject.SetActive(false);
                Destroy(bomb.gameObject);
            }
            foreach (var obstacle in eventObstacles)
                if (obstacle != null)
                    obstacle.Remove(false);
            eventObstacles.Clear();
        }

        private void CleanupVessel()
        {
            CleanupHazards();
            passengers.Clear();
            if (vessel == null)
                return;
            vessel.Boat.Destroyed.RemoveListener(OnBoatDestroyed);
            vessel.gameObject.SetActive(false);
            Destroy(vessel.gameObject);
            vessel = null;
        }

        private void ValidateDefinition()
        {
            if (store == null || definition == null || string.IsNullOrWhiteSpace(definition.id) || definition.vesselPrefab == null
                || definition.vesselPrefab.GetComponent<VoyageVessel>() == null || definition.stops.Length < 3
                || definition.stops[0].kind != VoyageStopKind.Island
                || definition.stops[definition.stops.Length - 1].kind != VoyageStopKind.Island
                || definition.initialPassengers.Length == 0)
                throw new InvalidDataException("Voyage configuration is incomplete.");
            var prefab = definition.vesselPrefab.GetComponent<VoyageVessel>();
            if (prefab.Boat == null || prefab.Player == null || prefab.DamageSystem == null || prefab.Boat.Deck == null
                || prefab.Player.BoatHealth != prefab.Boat || prefab.Player.MovementDeck != prefab.Boat.Deck
                || !(definition.sailingSeconds > 0f) || !float.IsFinite(definition.sailingSeconds)
                || !(definition.transitionSeconds > 0f) || !float.IsFinite(definition.transitionSeconds)
                || !(definition.boardingNoticeSeconds > 0f) || !float.IsFinite(definition.boardingNoticeSeconds))
                throw new InvalidDataException("Ship bindings or voyage durations are invalid.");
            Camera camera = Camera.main;
            var presentation = camera != null ? camera.GetComponent<VoyageScenePresentation>() : null;
            foreach (var stop in definition.stops)
                if (stop == null || string.IsNullOrWhiteSpace(stop.sceneName)
                    || (presentation != null && presentation.IsUnifiedScene
                        ? !presentation.CanPresentStop(stop) : !Application.CanStreamedLevelBeLoaded(stop.sceneName))
                    || !(stop.boardingChance >= 0f && stop.boardingChance <= 1f) || stop.maxBoardingPassengers < 0)
                    throw new InvalidDataException("Voyage scene or boarding configuration is invalid.");
            var profiles = new HashSet<string>();
            foreach (var profile in definition.initialPassengers.Concat(definition.passengerPool).Distinct())
                if (profile == null || string.IsNullOrWhiteSpace(profile.id) || profile.prefab == null || !profiles.Add(profile.id)
                    || profile.role == CrewRole.Combat && !(profile.prefab is CombatCrewMember)
                    || profile.role == CrewRole.Maintenance && !(profile.prefab is MaintenanceCrewMember)
                    || profile.coinsReward < 0 || profile.specialItemsReward < 0)
                    throw new InvalidDataException("Passenger job or profile ID is invalid.");
            foreach (var encounter in definition.randomEncounters.Concat(definition.stops.Select(stop => stop.fixedEncounter)).Where(item => item != null).Distinct())
            {
                if (encounter.waves == null || encounter.waves.Length == 0 || encounter.reward.coins < 0 || encounter.reward.specialItems < 0
                    || !(encounter.cameraSize > 0f) || !float.IsFinite(encounter.cameraSize))
                    throw new InvalidDataException("Combat event needs waves, valid rewards and a camera size.");
                foreach (var wave in encounter.waves)
                {
                    if (wave == null || wave.enemies == null || wave.enemies.Length == 0)
                        throw new InvalidDataException("Combat wave is empty.");
                    foreach (var group in wave.enemies)
                        if (group == null || group.prefab == null || group.count <= 0
                            || !float.IsFinite(group.spawnPosition.x) || !float.IsFinite(group.spawnPosition.y)
                            || group.prefab.AttackType == EnemyCharacter.AttackKind.Bomb
                                && (group.prefab.PreferredTarget != EnemyCharacter.TargetPreference.ShipFirst || group.prefab.BombPrefab == null)
                            || group.prefab.AttackType == EnemyCharacter.AttackKind.Projectile
                                && (group.prefab.PreferredTarget == EnemyCharacter.TargetPreference.ShipFirst || group.prefab.ProjectilePrefab == null))
                            throw new InvalidDataException("Combat wave has an invalid enemy spawn.");
                }
            }
            foreach (var special in definition.specialEvents.Select(entry => entry?.definition)
                .Concat(definition.stops.Select(stop => stop.fixedSpecialEvent)).Where(item => item != null).Distinct())
                if (special.obstaclePrefab == null || special.obstaclePrefab.GetComponent<DestructibleObstacle>() == null
                    || special.obstacleCount <= 0 || special.reward.coins < 0 || special.reward.specialItems < 0)
                    throw new InvalidDataException("Special event needs destructible obstacles and valid rewards.");
            VoyageEventPlanner.Build(definition, 0);
        }

        private CrewDefinition FindProfile(string id)
        {
            return definition.initialPassengers.Concat(definition.passengerPool).FirstOrDefault(profile => profile.id == id);
        }

        private void ValidatePassengers(VoyageCheckpoint checkpoint)
        {
            if (checkpoint.stopIndex >= definition.stops.Length)
                throw new InvalidDataException("Checkpoint route position is invalid.");
            Transform root = definition.vesselPrefab.transform;
            BoatDeck deck = definition.vesselPrefab.GetComponent<VoyageVessel>().Boat.Deck;
            Vector2 playerPosition = root.TransformPoint(checkpoint.playerLocalPosition);
            if (!float.IsFinite(playerPosition.x) || !float.IsFinite(playerPosition.y)
                || !deck.ContainsFloor(playerPosition))
                throw new InvalidDataException("Checkpoint player is outside the deck.");
            foreach (PassengerState passenger in checkpoint.passengers)
                if (passenger == null || FindProfile(passenger.profileId) == null
                    || passenger.destinationStopIndex <= checkpoint.stopIndex || passenger.destinationStopIndex >= definition.stops.Length
                    || definition.stops[passenger.destinationStopIndex].kind != VoyageStopKind.Island
                    || !float.IsFinite(passenger.localPosition.x) || !float.IsFinite(passenger.localPosition.y))
                    throw new InvalidDataException("Checkpoint passenger is invalid.");
                else
                {
                    Vector2 position = root.TransformPoint(passenger.localPosition);
                    if (!deck.ContainsFloor(position))
                        throw new InvalidDataException("Checkpoint passenger is outside the deck.");
                }
        }

        private void Update()
        {
            Camera camera = Camera.main;
            if (camera == null || definition == null)
                return;
            VoyageScenePresentation presentation = camera.GetComponent<VoyageScenePresentation>();
            float size = presentation != null ? presentation.CameraSize
                : Phase == VoyagePhase.Island || Phase == VoyagePhase.Departure ? 5f : 6f;
            if (Phase == VoyagePhase.Event && eventPlan[StopIndex].kind == VoyageEventKind.Combat)
                size = eventPlan[StopIndex].encounter.cameraSize;
            camera.orthographicSize = Mathf.MoveTowards(camera.orthographicSize, size, 2f * Time.deltaTime);
        }

        private void OnApplicationQuit()
        {
            if (ActiveRun && store != null)
                store.AbandonCheckpoint();
        }

        private void OnDestroy()
        {
            CleanupVessel();
        }
    }
}
