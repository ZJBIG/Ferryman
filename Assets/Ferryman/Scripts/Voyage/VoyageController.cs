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
    public enum VoyagePhase
    {
        Departure,
        Entering,
        Sailing,
        Event,
        Island,
        Leaving,
        Transition,
        Success,
        Failure,
        Error
    }

    public sealed partial class VoyageController : MonoBehaviour
    {
        private const float ObstacleThrowSeconds = 0.65f;
        private const float ObstacleThrowArc = 0.3f;
        private VoyageDefinition definition;
        private VoyageSaveStore store;
        private VoyageVessel vessel;
        private Coroutine routine;
        private readonly List<GameObject> obstacleCarriers = new List<GameObject>();
        private readonly List<WaveBubbles> waveBubbles = new List<WaveBubbles>();
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
        private string boardingDestination;

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
        public CrewDefinition BoardingProfile { get; private set; }
        public string BoardingDestination => BoardingProfile == null ? null : boardingDestination;

        public void Initialize(VoyageDefinition voyage, VoyageSaveStore saves)
        {
            definition = voyage;
            store = saves;
            DontDestroyOnLoad(gameObject);
            ValidateDefinition();
            Message = "空船启航，在途中岛屿接载乘客；启航前船只已完成整备。";
        }

        public void ReportError(Exception error)
        {
            if (routine != null)
                StopCoroutine(routine);
            routine = null;
            Phase = VoyagePhase.Error;
            BoardingProfile = null;
            Message = error.Message;
            Fade = 0f;
            if (vessel != null)
                vessel.SetActionsAllowed(false);
            CleanupHazards();
        }

        public bool StartVoyage(int voyageSeed)
        {
            if (ActiveRun || HasCheckpoint)
                return false;
            seed = voyageSeed;
            runId = Guid.NewGuid().ToString("N");
            pendingReward = default;
            deliveredPassengers = 0;
            CanRetrySettlement = false;
            LoadStory(definition.branchingRoutes && definition.storyCampaign ? 1 : 0);
            InitializeExpedition(definition.branchingRoutes, null);
            CreateVessel();
            RestoreSpecialCompanions(null);
            vessel.SetActionsAllowed(false, true);
            ActiveRun = true;
            StopIndex = 0;
            SaveCheckpoint();
            permanentUpgradeChosen = false;
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
            seed = checkpoint.seed;
            LoadStory(checkpoint.storyRound, checkpoint);
            InitializeExpedition(checkpoint.routePath != null && checkpoint.routePath.Length > 0, checkpoint);
            ValidatePassengers(checkpoint);
            runId = checkpoint.runId;
            pendingReward = new RewardData { coins = checkpoint.pendingCoins, specialItems = checkpoint.pendingSpecialItems };
            deliveredPassengers = checkpoint.deliveredPassengers;
            CreateVessel();
            vessel.Boat.RestoreState(checkpoint.ship);
            foreach (ObstacleState saved in checkpoint.obstacles ?? Array.Empty<ObstacleState>())
            {
                SeaEventDefinition source = definition.specialEvents.Select(entry => entry.definition)
                    .Concat(definition.stops.Select(stop => stop.fixedSpecialEvent))
                    .FirstOrDefault(item => item != null && item.id == saved.eventId)
                    ?? Resources.Load<SeaEventDefinition>("Voyage/Events/" + saved.eventId);
                if (source == null || source.id != saved.eventId || source.obstaclePrefab == null
                    || source.obstaclePrefab.GetComponent<TimedBomb>() != null)
                    throw new InvalidDataException("Carried obstacle has an unknown sea event.");
                var item = Instantiate(source.obstaclePrefab, vessel.transform);
                item.transform.localPosition = saved.localPosition;
                var obstacle = item.GetComponent<DestructibleObstacle>();
                obstacle.VoyageEventId = saved.eventId;
                obstacle.RestoreAppearance(saved.appearanceIndex);
                eventObstacles.Add(obstacle);
            }
            RestoreActorPosition(vessel.Player, checkpoint.playerLocalPosition);
            foreach (PassengerState saved in checkpoint.passengers)
            {
                if (HasPassenger(FindProfile(saved.profileId)))
                    continue;
                Passenger passenger = BoardPassenger(FindProfile(saved.profileId), saved.destinationStopIndex, false);
                passenger.EncountersAboard = Mathf.Max(0, saved.encountersAboard);
                RestoreActorPosition(passenger.Actor, saved.localPosition);
            }
            RestoreSpecialCompanions(checkpoint);
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
                if (Time.timeScale == 0f)
                {
                    yield return null;
                    continue;
                }
                object current = null;
                Exception failure = null;
                bool moved = false;
                try
                {
                    moved = pending.Peek().MoveNext();
                    if (moved)
                        current = pending.Peek().Current;
                }
                catch (Exception error)
                {
                    failure = error;
                }
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
            for (int index = firstStop; index < ActiveStops.Length && ActiveRun; index++)
            {
                StopIndex = index;
                VoyageStop stop = ActiveStops[index];
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
                    if (StoryRound > 0)
                        vessel.Player.BlockMouseAttacks = true;
                    if (!alreadyArrived && index > 0)
                    {
                        VoyageAudio.Play(Sound.Dock, 0.64f);
                        var farewells = DeliverPassengers(index);
                        if (StoryRound == 0)
                            yield return BoardAtIsland(stop, index);
                        else
                        {
                            SaveCheckpoint();
                            foreach (var profile in farewells)
                                yield return ShowStoryFarewell(profile);
                        }
                    }
                    if (StoryRound > 0)
                        yield return ShowIslandStory(index);
                    if (StoryRound > 0 && index > 0 && index < DestinationStop && !StoryDockOfferResolved)
                        yield return OfferStoryPassengerAtIsland(stop, index);
                    if (!ActiveRun)
                        yield break;
                    Message = "抵达 " + stop.displayName;
                    SaveCheckpoint();
                    if (StoryRound > 0 && index == DestinationStop)
                    {
                        storyPortReady = true;
                        while (ActiveRun && !advanceStoryPort)
                            yield return null;
                        storyPortReady = false;
                        advanceStoryPort = false;
                        if (!ActiveRun)
                            yield break;
                        if (StoryRound < FinalStoryRound)
                        {
                            AdvanceToNextRound();
                            index = -1;
                        }
                        else
                        {
                            StopIndex = BossStop;
                            SaveCheckpoint();
                        }
                        vessel.Player.BlockMouseAttacks = false;
                    }
                    else if (index == ActiveStops.Length - 1)
                    {
                        FinishRun(true);
                        yield break;
                    }
                    else if (IsExpedition)
                    {
                        vessel.Player.BlockMouseAttacks = true;
                        while (ActiveRun && StopIndex == index)
                            yield return null;
                        vessel.Player.BlockMouseAttacks = false;
                    }
                    else
                        yield return WaitActive(definition.transitionSeconds);
                }
                else
                {
                    SaveCheckpoint();
                    Phase = VoyagePhase.Sailing;
                    Message = "自动航行中";
                    if (index == 1 && specialCompanionOrder.Length > 0)
                        yield return IntroduceSpecialCompanion();
                    if (!ActiveRun)
                        yield break;
                    if (StoryRound > 0 && !IsBossLeg)
                        StartCoroutine(ShowSeaConversation());
                    yield return Sail(definition.sailingSeconds * 0.5f);
                    ClearStoryDialogue();
                    if (!ActiveRun)
                        yield break;
                    if (IsBossLeg)
                    {
                        BoardFinalAllies();
                        yield return ShowSpecialCompanionBriefing();
                    }
                    if (eventPlan[index].kind != VoyageEventKind.None)
                        yield return ExecuteEvent(eventPlan[index]);
                    if (IsBossLeg && ActiveRun)
                        yield return WaitActive(0.9f);
                    if (!ActiveRun)
                        yield break;
                    while (ActiveRun && FindObjectsOfType<TimedBomb>()
                        .Any(bomb => !bomb.GetComponent<DestructibleObstacle>().IsCleared))
                        yield return null;
                    if (!ActiveRun)
                        yield break;
                    if (eventPlan[index].kind == VoyageEventKind.Combat)
                        VoyageAudio.Play(Sound.BattleClear, 0.72f);
                    if (IsBossLeg)
                    {
                        yield return ShowFinalReturn();
                        if (ActiveRun)
                            FinishRun(true);
                        yield break;
                    }
                    Phase = VoyagePhase.Sailing;
                    Message = "继续航行";
                    yield return Sail(definition.sailingSeconds * 0.5f);
                }
                alreadyArrived = false;
                if (!ActiveRun)
                    yield break;
                CleanupHazards(true);
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
            if (planned.kind == VoyageEventKind.Combat)
            {
                vessel.Player.BlockMouseAttacks = true;
                yield return ShowEncounterConversation();
                if (!ActiveRun)
                    yield break;
                vessel.Player.BlockMouseAttacks = false;
            }
            Phase = VoyagePhase.Event;
            if (planned.kind == VoyageEventKind.Combat)
            {
                EncounterDefinition encounter = planned.encounter;
                Camera camera = Camera.main;
                while (ActiveRun && camera != null && (camera.orthographicSize != CombatCameraSize(camera, encounter)
                    || camera.transform.position.y != encounter.cameraHeight))
                    yield return null;
                SeaEventDefinition[] falling = IsBossLeg ? Array.Empty<SeaEventDefinition>() : definition.specialEvents.Select(entry => entry.definition)
                    .Where(item => item != null && item.obstaclePrefab != null).ToArray();
                int depth = IsExpedition ? RouteMap.Nodes[CurrentNodeId].depth : Mathf.Max(1, (StopIndex + 1) / 2);
                float dropInterval = Mathf.Max(3.5f, 7f - Mathf.Max(0, StoryRound - 1) - depth * 0.25f);
                int dropLimit = Mathf.Clamp(2 + (depth >= 4 ? 1 : 0) + (StoryRound >= 2 ? 1 : 0), 2, 4);
                int drops = 0;
                for (waveIndex = 0; waveIndex < encounter.waves.Length && ActiveRun; waveIndex++)
                {
                    Message = encounter.displayName + $"  第 {waveIndex + 1}/{encounter.waves.Length} 轮";
                    yield return SpawnWave(encounter.waves[waveIndex]);
                    int waveDrops = 0;
                    float nextDropAt = Time.time + Mathf.Min(1.2f, encounter.waveInterval * 0.5f);
                    var movementRandom = new Random(seed ^ StopIndex * 733 ^ waveIndex * 6271);
                    float diveAt = StoryRound >= 2 && !IsBossLeg
                        ? Time.time + Mathf.Lerp(7f, 10f, (float)movementRandom.NextDouble())
                        : float.PositiveInfinity;
                    while (ActiveRun && enemies.Any(enemy => enemy != null && enemy.Health > 0f))
                    {
                        if (Time.time >= diveAt)
                        {
                            diveAt = float.PositiveInfinity;
                            yield return RepositionSeaMonster(encounter.waves[waveIndex], movementRandom);
                        }
                        if (falling.Length > 0 && waveDrops < dropLimit && Time.time >= nextDropAt)
                        {
                            var dropRandom = new Random(seed ^ StopIndex * 3571 ^ drops * 9973);
                            yield return DispatchObstacles(falling[dropRandom.Next(falling.Length)], drops++);
                            waveDrops++;
                            nextDropAt = Time.time + dropInterval;
                        }
                        yield return null;
                    }
                }
                if (ActiveRun)
                    AddReward(encounter.reward);
            }
            else
            {
                SeaEventDefinition special = planned.specialEvent;
                Message = special.displayName;
                yield return DispatchObstacles(special);
                yield return WaitActive(ObstacleThrowSeconds);
                if (ActiveRun)
                    AddReward(special.reward);
            }
        }

        private IEnumerator SpawnWave(WaveDefinition wave)
        {
            enemies.Clear();
            Camera camera = Camera.main;
            if (camera == null)
                throw new InvalidOperationException("海怪生成需要主相机。");
            Physics2D.SyncTransforms();
            Bounds deck = vessel.Boat.Deck.GetComponent<EdgeCollider2D>().bounds;
            Vector2 half = WaveFootprint(wave, out int count);
            Vector2 viewHalf = new Vector2(camera.orthographicSize * camera.aspect, camera.orthographicSize);
            var random = IsBossLeg ? null : new Random(seed ^ StopIndex * 397 ^ waveIndex * 3571);
            var positions = FindWavePositions(deck, half, camera.transform.position, viewHalf, random);
            if (positions.Count < count)
                throw new InvalidOperationException("船的上侧和右侧没有足够的海怪生成空间。");
            if (IsBossLeg)
            {
                positions.Sort((left, right) => (left - (Vector2)deck.center).sqrMagnitude
                    .CompareTo((right - (Vector2)deck.center).sqrMagnitude));
            }
            else
            {
                for (int i = positions.Count - 1; i > 0; i--)
                {
                    int other = random.Next(i + 1);
                    (positions[i], positions[other]) = (positions[other], positions[i]);
                }
            }
            for (int i = 0; i < count; i++)
                waveBubbles.Add(WaveBubbles.Create(positions[i], IsBossLeg ? 1.8f : 1f));
            VoyageAudio.Play(Sound.WaterBubbles, waveIndex == 0 ? 0.71f : 0.62f, positions[0]);
            if (IsBossLeg)
                VoyageAudio.Play(Sound.TrashGather, 0.69f, positions[0]);
            yield return WaitActive(WaveBubbles.Duration);
            ClearWaveBubbles();
            if (!ActiveRun)
                yield break;

            int nextPosition = 0;
            foreach (EnemySpawn group in wave.enemies)
            {
                for (int unit = 0; unit < group.count; unit++)
                {
                    Vector2 position = positions[nextPosition++];
                    var enemy = Instantiate(group.prefab, position, Quaternion.identity);
                    enemy.InitializeEnemy(vessel.Boat, vessel.Player, vessel.DamageSystem);
                    enemies.Add(enemy);
                }
            }
        }

        private IEnumerator RepositionSeaMonster(WaveDefinition wave, Random random)
        {
            Camera camera = Camera.main;
            if (camera == null)
                yield break;
            var shooters = enemies.Where(enemy => enemy != null && enemy.isActiveAndEnabled && enemy.Health > 0f
                && enemy.ActionsEnabled && enemy.AttackType == EnemyCharacter.AttackKind.Projectile).ToArray();
            if (shooters.Length == 0)
                yield break;
            EnemyCharacter shooter = shooters[random.Next(shooters.Length)];
            Bounds deck = vessel.Boat.Deck.GetComponent<EdgeCollider2D>().bounds;
            Vector2 half = WaveFootprint(wave, out _);
            Vector2 viewHalf = new Vector2(camera.orthographicSize * camera.aspect, camera.orthographicSize);
            Vector2 origin = shooter.transform.position;
            Vector2 separation = half * 2f + Vector2.one * 0.8f;
            var openings = FindWavePositions(deck, half, camera.transform.position, viewHalf, random)
                .Where(at => (at.x > deck.max.x) == (origin.x > deck.max.x)
                    && (at - origin).sqrMagnitude >= 4f && (at - origin).sqrMagnitude <= 81f
                    && !enemies.Any(other => other != null && other != shooter && other.Health > 0f
                        && Mathf.Abs(other.transform.position.x - at.x) < separation.x
                        && Mathf.Abs(other.transform.position.y - at.y) < separation.y)).ToArray();
            if (openings.Length > 0)
                yield return shooter.DiveTo(openings[random.Next(openings.Length)]);
        }

        private static Vector2 WaveFootprint(WaveDefinition wave, out int count)
        {
            Vector2 half = Vector2.zero;
            count = 0;
            foreach (EnemySpawn group in wave.enemies)
            {
                Bounds visual = group.prefab.SpawnBounds;
                Vector3 origin = group.prefab.transform.position;
                half.x = Mathf.Max(half.x, Mathf.Abs(visual.min.x - origin.x), Mathf.Abs(visual.max.x - origin.x));
                half.y = Mathf.Max(half.y, Mathf.Abs(visual.min.y - origin.y), Mathf.Abs(visual.max.y - origin.y));
                count += group.count;
            }
            return half;
        }

        private static List<Vector2> FindWavePositions(Bounds deck, Vector2 half, Vector2 viewCenter, Vector2 viewHalf,
            Random random = null)
        {
            const float spacing = 0.6f;
            const float jitter = 0.62f;
            const float shipClearance = 2f;
            Vector2 step = half * 2f + Vector2.one * (spacing + jitter);
            Vector2 min = Vector2.Max(viewCenter - viewHalf + half + Vector2.one * 0.3f,
                (Vector2)deck.min + half);
            Vector2 max = viewCenter + viewHalf - half - Vector2.one * 0.3f;
            var positions = new List<Vector2>();
            float top = Mathf.Max(min.y, deck.max.y + half.y + shipClearance);
            for (int side = 0; side < 2; side++)
            {
                Vector2 regionMin = side == 0 ? new Vector2(min.x, top)
                    : new Vector2(Mathf.Max(min.x, deck.max.x + half.x + shipClearance), min.y);
                Vector2 regionMax = side == 0 ? max : new Vector2(max.x, Mathf.Min(max.y, top - step.y));
                for (float y = regionMin.y; y <= regionMax.y; y += step.y)
                {
                    for (float x = regionMin.x; x <= regionMax.x; x += step.x)
                    {
                        // A slot has at least the reserved spacing even when adjacent offsets differ.
                        float shiftX = random == null ? 0f : (float)random.NextDouble() * Mathf.Min(jitter, regionMax.x - x);
                        float shiftY = random == null ? 0f : (float)random.NextDouble() * Mathf.Min(jitter, regionMax.y - y);
                        positions.Add(new Vector2(x + shiftX, y + shiftY));
                    }
                }
            }
            return positions;
        }

        private IEnumerator DispatchObstacles(SeaEventDefinition special, int combatDrop = -1)
        {
            BoatDeck deck = vessel.Boat.Deck;
            int count = combatDrop < 0 ? special.obstacleCount
                : StoryRound >= 2 || IsExpedition && RouteMap.Nodes[CurrentNodeId].depth >= 5 ? 2 : 1;
            Vector2 footprint = special.obstaclePrefab.GetComponent<DestructibleObstacle>().Footprint;
            Vector2Int center = deck.WorldToCell(Vector2.zero);
            var cells = new List<Vector2Int>();
            Vector2Int grid = deck.GridSize;
            for (int x = 0; x < grid.x; x++)
            {
                for (int y = 0; y < grid.y; y++)
                {
                    Vector2Int cell = new Vector2Int(x, y);
                    if (BoatDeck.CellDistance(center, cell) > 1)
                        cells.Add(cell);
                }
            }
            var random = new Random(seed ^ StopIndex * 397 ^ (combatDrop >= 0 ? combatDrop * 3571 : 0));
            float heading = (float)random.NextDouble() * 2f * Mathf.PI;
            Vector2 direction = new Vector2(Mathf.Cos(heading), Mathf.Sin(heading));
            var landings = new List<Rect>();
            Physics2D.SyncTransforms();
            while (landings.Count < count && cells.Count > 0)
            {
                int selected = random.Next(cells.Count);
                Vector2 position = deck.CellToWorld(cells[selected]);
                cells.RemoveAt(selected);
                var landing = new Rect(position - footprint * 0.5f, footprint);
                if (landings.Any(other => other.Overlaps(landing)) || !deck.CanPlace(position, footprint))
                    continue;
                landings.Add(landing);
            }
            if (landings.Count == 0)
                yield break;

            Camera view = Camera.main;
            Sprite sprite = definition.obstacleCarrier;
            Sprite[] frames = definition.obstacleCarrierFrames ?? Array.Empty<Sprite>();
            if (view == null || sprite == null)
                throw new InvalidOperationException("海鸥投放需要主相机和海鸥素材。");
            landings.Sort((left, right) => Vector2.Dot(left.center, direction).CompareTo(Vector2.Dot(right.center, direction)));
            Vector2 flybyCenter = landings.Aggregate(Vector2.zero, (sum, landing) => sum + landing.center) / landings.Count;
            Vector2 viewCenter = view.transform.position;
            Vector2 viewHalf = new Vector2(view.orthographicSize * view.aspect, view.orthographicSize);
            var viewArea = new Rect(viewCenter - viewHalf, viewHalf * 2f);
            float screenCenter = Vector2.Dot(viewCenter - flybyCenter, direction);
            float screenRadius = Mathf.Abs(direction.x) * viewHalf.x + Mathf.Abs(direction.y) * viewHalf.y
                + sprite.bounds.extents.magnitude * 1.3f;
            const float throwLead = 2.2f;
            const float carrierSpacing = 1.95f;
            float firstRelease = Vector2.Dot(landings[0].center - flybyCenter, direction) - throwLead;
            float lastRelease = Vector2.Dot(landings[landings.Count - 1].center - flybyCenter, direction) - throwLead
                + (landings.Count - 1) * carrierSpacing;
            float distance = Mathf.Min(screenCenter - screenRadius, firstRelease);
            float finish = Mathf.Max(screenCenter + screenRadius + (landings.Count - 1) * carrierSpacing, lastRelease);

            var carriers = new GameObject[landings.Count];
            var visuals = new SpriteRenderer[landings.Count];
            var offsets = new Vector2[landings.Count];
            var called = new bool[landings.Count];
            var released = new bool[landings.Count];
            for (int i = 0; i < carriers.Length; i++)
            {
                var carrier = new GameObject("Obstacle Seagull");
                carriers[i] = carrier;
                obstacleCarriers.Add(carrier);
                var visual = carrier.AddComponent<SpriteRenderer>();
                visuals[i] = visual;
                visual.sprite = sprite;
                visual.sortingOrder = 16;
                visual.flipX = direction.x < 0f;
                carrier.transform.rotation = Quaternion.Euler(0f, 0f, heading * Mathf.Rad2Deg + (visual.flipX ? 180f : 0f));
                carrier.transform.localScale = new Vector3(1.3f, 1.3f, 1f);
                Vector2 offset = landings[i].center - flybyCenter;
                offsets[i] = offset - direction * (Vector2.Dot(offset, direction) + i * carrierSpacing);
            }
            float flybyStartedAt = Time.time;
            while (ActiveRun)
            {
                for (int i = 0; i < carriers.Length; i++)
                {
                    GameObject carrier = carriers[i];
                    if (frames.Length > 0)
                        visuals[i].sprite = frames[(Mathf.FloorToInt((Time.time - flybyStartedAt) * 8f) + i) % frames.Length];
                    Vector2 carrierPosition = flybyCenter + direction * distance + offsets[i];
                    carrier.transform.position = carrierPosition;
                    if (!called[i] && viewArea.Contains(carrierPosition))
                    {
                        VoyageAudio.Play(Sound.Gull, volume: 0.84f, position: carrierPosition);
                        called[i] = true;
                    }
                    Vector2 destination = landings[i].center;
                    float releaseDistance = Vector2.Dot(destination - flybyCenter, direction) - throwLead + i * carrierSpacing;
                    if (released[i] || distance < releaseDistance)
                        continue;
                    Vector2 origin = carrierPosition
                        + (Vector2)carrier.transform.TransformVector(Vector2.down * sprite.bounds.extents.y * 0.66f);
                    var item = Instantiate(special.obstaclePrefab, origin, Quaternion.identity, vessel.transform);
                    var obstacle = item.GetComponent<DestructibleObstacle>();
                    obstacle.VoyageEventId = special.id;
                    var bomb = item.GetComponent<TimedBomb>();
                    VoyageAudio.Play(bomb != null ? Sound.BombThrow : Sound.GullRelease, position: origin);
                    if (bomb != null)
                        bomb.Launch(vessel.Boat, destination, ObstacleThrowSeconds, ObstacleThrowArc);
                    else
                        obstacle.FlyTo(deck, destination, ObstacleThrowSeconds, ObstacleThrowArc);
                    eventObstacles.Add(obstacle);
                    released[i] = true;
                }
                if (distance >= finish)
                    break;
                yield return null;
                distance = Mathf.MoveTowards(distance, finish, 12f * Time.deltaTime);
            }
            foreach (var carrier in carriers)
            {
                obstacleCarriers.Remove(carrier);
                Destroy(carrier);
            }
        }

        private Passenger BoardPassenger(CrewDefinition profile, int destination, bool playSound = true)
        {
            if (profile == null || HasPassenger(profile) || destination <= 0 || destination >= ActiveStops.Length
                || ActiveStops[destination].kind != VoyageStopKind.Island)
                return null;
            var passenger = SpawnCrew(profile, destination);
            passengers.Add(passenger);
            RefreshPassengerConflict();
            if (playSound)
                VoyageAudio.Play(Sound.Board, 0.56f, passenger.transform.position);
            return passenger;
        }

        private Passenger SpawnCrew(CrewDefinition profile, int destination, Vector2? position = null)
        {
            var footprint = profile.prefab.GetComponent<BoxCollider2D>();
            Character actor = Instantiate(profile.prefab, position ?? FindBoardingPosition(footprint),
                Quaternion.identity, vessel.transform);
            actor.ApplyCrewAppearance(profile);
            var passenger = actor.GetComponent<Passenger>() ?? actor.gameObject.AddComponent<Passenger>();
            passenger.Initialize(profile, destination);
            if (actor is CombatCrewMember combat)
                combat.InitializeCrew(vessel.Boat, vessel.DamageSystem, profile);
            else if (actor is MaintenanceCrewMember maintenance)
                maintenance.InitializeCrew(vessel.Boat, profile);
            return passenger;
        }

        private bool HasPassenger(CrewDefinition profile) => passengers.Any(passenger => passenger.Definition.id == profile.id);

        public bool CanBoardRole(CrewRole role) => StoryRound > 0
            ? StoryRescueProfiles.Any(profile => profile.role == role)
            : definition.passengerPool.Any(profile => profile.role == role && !HasPassenger(profile));

        public static bool CanDisembarkAt(VoyageIslandKind kind) => kind != VoyageIslandKind.Battle
            && kind != VoyageIslandKind.Elite && kind != VoyageIslandKind.Departure;

        private Vector2 FindBoardingPosition(BoxCollider2D footprint)
        {
            BoatDeck deck = vessel.Boat.Deck;
            Vector2Int grid = deck.GridSize;
            Physics2D.SyncTransforms();
            for (int y = grid.y - 1; y >= 0; y--)
            {
                for (int x = 0; x < grid.x; x++)
                {
                    Vector2 position = deck.CellToWorld(new Vector2Int(x, y));
                    if (deck.CanPlace(position, footprint.size + Vector2.one * 0.05f))
                        return position - footprint.offset;
                }
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
            if (index <= 0 || stop.kind != VoyageStopKind.Island)
                yield break;
            var futureIslands = Enumerable.Range(index + 1, ActiveStops.Length - index - 1)
                .Where(next => ActiveStops[next].kind == VoyageStopKind.Island).ToArray();
            if (futureIslands.Length == 0 || definition.passengerPool.Length == 0 || stop.maxBoardingPassengers <= 0)
                yield break;
            var random = new Random(seed ^ index * 9973);
            if (random.NextDouble() >= stop.boardingChance)
                yield break;
            int count = random.Next(1, stop.maxBoardingPassengers + 1);
            for (int i = 0; i < count && ActiveRun; i++)
            {
                CrewDefinition[] available = definition.passengerPool.Where(profile => !HasPassenger(profile)).ToArray();
                if (available.Length == 0)
                    yield break;
                CrewDefinition profile = available[random.Next(available.Length)];
                int destination = futureIslands[random.Next(futureIslands.Length)];
                BoardingProfile = profile;
                boardingDestination = ActiveStops[destination].displayName;
                Message = profile.displayName + " 将上船 · 目的地 " + boardingDestination + $" · 送达报酬 {profile.coinsReward} 金币";
                VoyageAudio.Play(Sound.Dialogue, 0.51f);
                yield return WaitActive(definition.boardingNoticeSeconds);
                BoardingProfile = null;
                if (ActiveRun)
                    BoardPassenger(profile, destination);
            }
        }

        private IReadOnlyList<CrewDefinition> DeliverPassengers(int index)
        {
            if (ActiveStops[index].kind != VoyageStopKind.Island || IsExpedition && !CanDisembarkAt(CurrentIsland))
                return Array.Empty<CrewDefinition>();
            var farewells = new List<CrewDefinition>();
            int before = deliveredPassengers;
            for (int i = passengers.Count - 1; i >= 0; i--)
            {
                Passenger passenger = passengers[i];
                if (passenger.DestinationStopIndex > index)
                    continue;
                AddReward(new RewardData { coins = passenger.Definition.coinsReward, specialItems = passenger.Definition.specialItemsReward });
                deliveredPassengers++;
                if (StoryRound > 0 && IsStoryProfile(passenger.Definition.id) && deliveredStoryProfiles.Add(passenger.Definition.id))
                    farewells.Add(passenger.Definition);
                passengers.RemoveAt(i);
                passenger.Disembark();
            }
            if (before != deliveredPassengers)
            {
                RefreshPassengerConflict();
                VoyageAudio.Play(Sound.Reward, 0.65f);
            }
            return farewells;
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
            ApplyUpgrades();
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
                storyRound = StoryRound,
                deliveredStoryProfiles = StoryRound > 0
                    ? deliveredStoryProfiles.OrderBy(id => id, StringComparer.Ordinal).ToArray() : null,
                storyDockOfferResolved = StoryDockOfferResolved,
                specialCompanionOrder = specialCompanionOrder.Length > 0 ? specialCompanionOrder : null,
                specialCompanions = CaptureSpecialCompanions(),
                pendingCoins = pendingReward.coins,
                pendingSpecialItems = pendingReward.specialItems,
                deliveredPassengers = deliveredPassengers,
                routePath = IsExpedition ? routePath.ToArray() : null,
                runUpgrades = IsExpedition ? runUpgrades : null,
                islandResolved = IslandResolved,
                upgradeChoices = UpgradeChoices,
                playerLocalPosition = vessel.Player.transform.localPosition,
                ship = vessel.Boat.CaptureState(),
                obstacles = vessel.GetComponentsInChildren<DestructibleObstacle>().Where(obstacle => !obstacle.IsCleared
                    && obstacle.GetComponent<TimedBomb>() == null).Select(obstacle => new ObstacleState
                {
                    eventId = obstacle.VoyageEventId,
                    localPosition = vessel.transform.InverseTransformPoint(obstacle.WorldLandingPosition),
                    appearanceIndex = obstacle.AppearanceIndex
                }).ToArray(),
                passengers = passengers.Select(passenger => new PassengerState
                {
                    profileId = passenger.Definition.id,
                    destinationStopIndex = passenger.DestinationStopIndex,
                    encountersAboard = passenger.EncountersAboard,
                    localPosition = passenger.transform.localPosition
                }).ToArray()
            });
        }

        private void OnBoatDestroyed()
        {
            if (!ActiveRun)
                return;
            VoyageAudio.Play(Sound.Failure, 0.78f);
            if (routine != null)
                StopCoroutine(routine);
            FinishRun(false);
        }

        private void FinishRun(bool success)
        {
            BoardingProfile = null;
            vessel.SetActionsAllowed(false);
            CleanupHazards();
            settlementSuccess = success;
            CanRetrySettlement = true;
            try
            {
                store.Settle(runId, pendingReward);
            }
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
            BoardingProfile = null;
            if (routine != null)
                StopCoroutine(routine);
            ActiveRun = false;
            routine = null;
            store.AbandonCheckpoint();
            CanRetrySettlement = false;
            pendingReward = default;
            deliveredPassengers = 0;
            LoadStory(0);
            CleanupVessel();
            ClearExpedition();
            routine = StartCoroutine(RunSafely(ReturnToDeparture()));
        }

        public void ReturnHome()
        {
            if (ActiveRun)
                return;
            LoadStory(0);
            CleanupVessel();
            ClearExpedition();
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
            Message = "空船启航，在途中岛屿接载乘客；启航前船只已完成整备。";
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

        private void CleanupHazards(bool keepObstacles = false)
        {
            ClearWaveBubbles();
            foreach (var carrier in obstacleCarriers)
            {
                if (carrier == null)
                    continue;
                carrier.SetActive(false);
                Destroy(carrier);
            }
            obstacleCarriers.Clear();
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
            var carried = vessel != null ? vessel.GetComponentsInChildren<DestructibleObstacle>() : Array.Empty<DestructibleObstacle>();
            foreach (var obstacle in eventObstacles.Concat(carried).Distinct())
                if (obstacle != null && (!keepObstacles || obstacle.GetComponent<TimedBomb>() != null))
                {
                    obstacle.gameObject.SetActive(false);
                    Destroy(obstacle.gameObject);
                }
            eventObstacles.RemoveAll(obstacle => !keepObstacles || obstacle == null || obstacle.IsCleared
                || obstacle.GetComponent<TimedBomb>() != null);
        }

        private void ClearWaveBubbles()
        {
            foreach (WaveBubbles bubbles in waveBubbles)
            {
                if (bubbles == null)
                    continue;
                bubbles.gameObject.SetActive(false);
                Destroy(bubbles.gameObject);
            }
            waveBubbles.Clear();
        }

        private void CleanupVessel()
        {
            CleanupHazards();
            passengers.Clear();
            specialCompanionOrder = Array.Empty<string>();
            specialCompanions.Clear();
            companionActors.Clear();
            waitingFinalAllies.Clear();
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
                || definition.stops[definition.stops.Length - 1].kind != VoyageStopKind.Island)
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
            foreach (var encounter in definition.randomEncounters.Concat(definition.stops.Select(stop => stop.fixedEncounter))
                .Append(definition.finalBossEncounter).Where(item => item != null).Distinct())
            {
                if (encounter.waves == null || encounter.waves.Length == 0 || encounter.reward.coins < 0 || encounter.reward.specialItems < 0
                    || encounter.waveInterval < 0.1f || !float.IsFinite(encounter.waveInterval)
                    || !(encounter.cameraSize > 0f) || !float.IsFinite(encounter.cameraSize) || !float.IsFinite(encounter.cameraHeight))
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
                                && group.prefab.ProjectilePrefab == null)
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
            return definition.initialPassengers.Concat(definition.passengerPool).FirstOrDefault(profile => profile.id == id)
                ?? FindStoryProfile(id);
        }

        private void ValidatePassengers(VoyageCheckpoint checkpoint)
        {
            if (checkpoint.stopIndex >= ActiveStops.Length)
                throw new InvalidDataException("Checkpoint route position is invalid.");
            Transform root = definition.vesselPrefab.transform;
            BoatDeck deck = definition.vesselPrefab.GetComponent<VoyageVessel>().Boat.Deck;
            Vector2 playerPosition = root.TransformPoint(checkpoint.playerLocalPosition);
            if (!float.IsFinite(playerPosition.x) || !float.IsFinite(playerPosition.y)
                || !deck.ContainsFloor(playerPosition))
                throw new InvalidDataException("Checkpoint player is outside the deck.");
            foreach (PassengerState passenger in checkpoint.passengers)
                if (passenger == null || FindProfile(passenger.profileId) == null
                    || passenger.destinationStopIndex <= 0 || passenger.destinationStopIndex >= ActiveStops.Length
                    || ActiveStops[passenger.destinationStopIndex].kind != VoyageStopKind.Island
                    || !float.IsFinite(passenger.localPosition.x) || !float.IsFinite(passenger.localPosition.y))
                    throw new InvalidDataException("Checkpoint passenger is invalid.");
                else
                {
                    Vector2 position = root.TransformPoint(passenger.localPosition);
                    if (!deck.ContainsFloor(position))
                        throw new InvalidDataException("Checkpoint passenger is outside the deck.");
                }
        }

        private float CombatCameraSize(Camera camera, EncounterDefinition encounter)
        {
            // Preserve the encounter's horizontal room when the viewport is narrower than 16:9.
            float size = encounter.cameraSize * Mathf.Max(1f, (16f / 9f) / camera.aspect);
            if (vessel == null)
                return size;
            Bounds deck = vessel.Boat.Deck.GetComponent<EdgeCollider2D>().bounds;
            Vector2 center = new Vector2(camera.transform.position.x, encounter.cameraHeight);
            foreach (WaveDefinition wave in encounter.waves)
            {
                Vector2 half = WaveFootprint(wave, out int count);
                while (size < 24f && FindWavePositions(deck, half, center, new Vector2(size * camera.aspect, size)).Count < count)
                    size += 0.5f;
            }
            return size;
        }

        private void Update()
        {
            PlaceWaitingCompanions();
            Camera camera = Camera.main;
            if (camera == null || definition == null)
                return;
            VoyageScenePresentation presentation = camera.GetComponent<VoyageScenePresentation>();
            float size = presentation != null ? presentation.GetCameraSize(camera)
                : Phase == VoyagePhase.Island || Phase == VoyagePhase.Departure ? 5f : 6f;
            Vector3 position = camera.transform.position;
            float height = presentation != null && presentation.IsUnifiedScene ? presentation.GetCameraHeight(size) : position.y;
            if (Phase == VoyagePhase.Event && eventPlan[StopIndex].kind == VoyageEventKind.Combat)
            {
                size = CombatCameraSize(camera, eventPlan[StopIndex].encounter);
                height = eventPlan[StopIndex].encounter.cameraHeight;
            }
            else if (Phase == VoyagePhase.Sailing)
                height = 0f;

            float step = 4f * Time.deltaTime;
            position.y = Mathf.MoveTowards(position.y, height, step);
            camera.transform.position = position;
            camera.orthographicSize = Mathf.MoveTowards(camera.orthographicSize, size, step);
        }

        private void OnApplicationQuit()
        {
            if (ActiveRun && store != null)
                store.AbandonCheckpoint();
        }

        private void OnDestroy()
        {
            CleanupVessel();
            ClearExpedition();
        }
    }
}
