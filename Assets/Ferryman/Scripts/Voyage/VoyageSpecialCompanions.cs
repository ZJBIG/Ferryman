using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ferryman.Characters;
using Ferryman.Combat;
using UnityEngine;

namespace Ferryman.Voyage
{
    public sealed partial class VoyageController
    {
        private string[] specialCompanionOrder = Array.Empty<string>();
        private readonly List<SpecialCompanionState> specialCompanions = new List<SpecialCompanionState>();
        private readonly Dictionary<string, SpecialCompanion> companionActors = new Dictionary<string, SpecialCompanion>();
        private readonly List<CrewDefinition> waitingFinalAllies = new List<CrewDefinition>();
        private float retryCompanionPlacementAt;

        private CrewDefinition FindSpecialCompanion(string id) =>
            definition.specialCompanions.FirstOrDefault(profile => profile.id == id);

        private void RestoreSpecialCompanions(VoyageCheckpoint checkpoint)
        {
            if (StoryRound == 0 || definition.specialCompanions == null || definition.specialCompanions.Length == 0)
                return;

            CrewDefinition[] profiles = definition.specialCompanions;
            if (profiles.Length != 4 || profiles.Any(profile => profile == null || string.IsNullOrEmpty(profile.id)
                || !(profile.prefab is SpecialCompanion) || profile.portrait == null || profile.deckSprite == null
                || profile.walkFrames == null || profile.walkFrames.Length != 4 || profile.walkFrames.Any(frame => frame == null)
                || profile.skillFrames == null || profile.skillFrames.Length != 4 || profile.skillFrames.Any(frame => frame == null))
                || profiles.Select(profile => profile.id).Distinct(StringComparer.Ordinal).Count() != 4
                || profiles.Any(profile => profile.id != "alo" && profile.id != "ayuan" && profile.id != "wenjia" && profile.id != "ayu"))
                throw new InvalidDataException("航程缺少四位特殊船员的完整配置。");

            specialCompanionOrder = checkpoint?.specialCompanionOrder;
            if (specialCompanionOrder == null || specialCompanionOrder.Length == 0)
            {
                string[] ids = profiles.Select(profile => profile.id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
                var random = new System.Random(seed ^ 849127);
                for (int i = ids.Length - 1; i > 0; i--)
                {
                    int swap = random.Next(i + 1);
                    (ids[i], ids[swap]) = (ids[swap], ids[i]);
                }
                specialCompanionOrder = ids.Take(FinalStoryRound).ToArray();
            }
            if (specialCompanionOrder.Length != FinalStoryRound
                || specialCompanionOrder.Distinct(StringComparer.Ordinal).Count() != FinalStoryRound
                || specialCompanionOrder.Any(id => FindSpecialCompanion(id) == null))
                throw new InvalidDataException("检查点包含无效的特殊船员顺序。");

            int eligibleDays = checkpoint != null && checkpoint.stopIndex == 0 ? StoryRound - 1 : StoryRound;
            foreach (SpecialCompanionState saved in checkpoint?.specialCompanions ?? Array.Empty<SpecialCompanionState>())
            {
                if (saved == null || !specialCompanionOrder.Take(eligibleDays).Contains(saved.profileId)
                    || specialCompanions.Any(item => item.profileId == saved.profileId)
                    || !float.IsFinite(saved.localPosition.x) || !float.IsFinite(saved.localPosition.y))
                    throw new InvalidDataException("检查点包含无效的特殊船员记录。");
                specialCompanions.Add(saved);
                TryPlaceCompanion(saved);
            }
        }

        private SpecialCompanionState[] CaptureSpecialCompanions()
        {
            if (specialCompanionOrder.Length == 0)
                return null;
            foreach (SpecialCompanionState state in specialCompanions)
            {
                if (!companionActors.TryGetValue(state.profileId, out SpecialCompanion actor))
                    continue;
                state.hasDeckPosition = true;
                state.localPosition = actor.transform.localPosition;
            }
            return specialCompanions.ToArray();
        }

        private Vector2 CompanionLandingPoint()
        {
            Bounds deck = vessel.Boat.Deck.GetComponent<EdgeCollider2D>().bounds;
            return new Vector2(deck.max.x - 0.9f, deck.center.y);
        }

        private bool TryGetCompanionPosition(CrewDefinition profile, Vector2 preferred, out Vector2 position)
        {
            var bounds = profile.prefab.GetComponent<BoxCollider2D>();
            Physics2D.SyncTransforms();
            if (!vessel.Boat.Deck.TryFindPlacement(preferred + bounds.offset, bounds.size, null, out position))
                return false;
            position -= bounds.offset;
            return true;
        }

        private bool TryPlaceCompanion(SpecialCompanionState state)
        {
            CrewDefinition profile = FindSpecialCompanion(state.profileId);
            Vector2 preferred = state.hasDeckPosition
                ? vessel.transform.TransformPoint(state.localPosition) : CompanionLandingPoint();
            if (!TryGetCompanionPosition(profile, preferred, out Vector2 position))
                return false;

            SpecialCompanionSkill skill = profile.id switch
            {
                "alo" => SpecialCompanionSkill.Kick,
                "ayuan" => SpecialCompanionSkill.Mark,
                "wenjia" => SpecialCompanionSkill.Throw,
                _ => SpecialCompanionSkill.Repair
            };
            Sprite[] kickFrames = skill == SpecialCompanionSkill.Kick
                ? Enumerable.Range(1, 4).Select(i => Resources.Load<Sprite>($"Voyage/SpecialCompanions/alo/kick{i:00}")).ToArray()
                : null;
            if (kickFrames != null && kickFrames.Any(frame => frame == null))
                throw new InvalidDataException("阿洛缺少踢腿序列帧。");

            var actor = (SpecialCompanion)Instantiate(profile.prefab, position, Quaternion.identity, vessel.transform);
            actor.name = profile.displayName;
            actor.InitializeCompanion(vessel.Boat, vessel.DamageSystem, vessel.Player, profile, skill, kickFrames);
            companionActors.Add(profile.id, actor);
            return true;
        }

        private void PlaceWaitingCompanions()
        {
            if (!ActiveRun || vessel == null || Time.timeScale == 0f
                || (specialCompanions.Count == companionActors.Count && waitingFinalAllies.Count == 0)
                || AwaitingStoryDialogue || Time.time < retryCompanionPlacementAt
                || Phase != VoyagePhase.Sailing && Phase != VoyagePhase.Event && Phase != VoyagePhase.Island)
                return;
            retryCompanionPlacementAt = Time.time + 0.5f;
            foreach (SpecialCompanionState state in specialCompanions)
                if (!companionActors.ContainsKey(state.profileId))
                    TryPlaceCompanion(state);
            PlaceFinalAllies();
        }

        private void PlaceFinalAllies()
        {
            bool placed = false;
            for (int i = 0; i < waitingFinalAllies.Count;)
            {
                CrewDefinition profile = waitingFinalAllies[i];
                if (!TryGetCompanionPosition(profile, CompanionLandingPoint(), out Vector2 position))
                {
                    i++;
                    continue;
                }
                SpawnCrew(profile, DestinationStop, position);
                waitingFinalAllies.RemoveAt(i);
                placed = true;
            }
            if (placed)
                RefreshPassengerConflict();
        }

        private IEnumerator IntroduceSpecialCompanion()
        {
            string id = specialCompanionOrder[StoryRound - 1];
            if (specialCompanions.Any(state => state.profileId == id))
                yield break;

            CrewDefinition profile = FindSpecialCompanion(id);
            yield return WaitActive(0.35f);
            if (!ActiveRun)
                yield break;
            vessel.Player.BlockMouseAttacks = true;
            vessel.SetActionsAllowed(false);
            Vector2 landing = CompanionLandingPoint();
            bool hasLanding = TryGetCompanionPosition(profile, landing, out Vector2 safeLanding);
            if (hasLanding)
                landing = safeLanding;
            Bounds deck = vessel.Boat.Deck.GetComponent<EdgeCollider2D>().bounds;
            float side = landing.x >= deck.center.x ? 1f : -1f;
            Vector2 water = new Vector2((side > 0f ? deck.max.x : deck.min.x) + side * 0.65f, landing.y - 0.25f);
            Vector2 end = hasLanding ? landing : water - Vector2.right * side * 0.4f;
            var visual = new GameObject("Special Companion Arrival").AddComponent<SpriteRenderer>();
            visual.transform.SetParent(vessel.transform);
            visual.sprite = profile.walkFrames[2];
            visual.sortingOrder = 6;
            visual.flipX = side > 0f;
            var ripple = WaveBubbles.Create(water, 0.55f);
            ripple.transform.SetParent(vessel.transform, true);
            VoyageAudio.Play(Sound.WaterSplash, 0.65f, water);
            const float jumpDuration = 0.65f;
            for (float elapsed = 0f; elapsed < jumpDuration && ActiveRun; elapsed += Time.deltaTime)
            {
                float progress = elapsed / jumpDuration;
                visual.transform.position = Vector2.Lerp(water, end, progress)
                    + Vector2.up * Mathf.Sin(progress * Mathf.PI) * 1.05f;
                yield return null;
            }

            visual.transform.position = end;
            visual.sprite = profile.deckSprite;
            Destroy(ripple.gameObject);
            VoyageAudio.Play(Sound.Board, 0.58f, end);
            string reply = StoryRound switch
            {
                1 => "兔子船长：上来吧。我们靠岛接人，你顺路指给我看。",
                2 => "兔子船长：先抓稳栏杆。你指方向，我来记。",
                _ => "兔子船长：跟我到终点港。渡船到站以后，还有一趟外港要跑。"
            };
            string standby = hasLanding ? null : "兔子船长：甲板挤，先扶住外梯。有位置我就接你上来。";
            yield return ShowStoryConversation(profile.displayName + "：" + CompanionArrivalLine(id, StoryRound),
                hasLanding ? reply : standby);
            Destroy(visual.gameObject);
            if (!ActiveRun)
                yield break;

            var state = new SpecialCompanionState
            {
                profileId = id,
                hasDeckPosition = hasLanding,
                localPosition = hasLanding ? (Vector2)vessel.transform.InverseTransformPoint(landing) : Vector2.zero
            };
            specialCompanions.Add(state);
            TryPlaceCompanion(state);
            vessel.SetActionsAllowed(true);
            vessel.Player.BlockMouseAttacks = false;
            SaveCheckpoint();
        }

        private string FirstCompanionCombatLine()
        {
            if (specialCompanionOrder.Length == 0)
                return null;
            string id = specialCompanionOrder[StoryRound - 1];
            if (!companionActors.ContainsKey(id))
                return null;
            string line = id switch
            {
                "alo" => "你打海里的。敢爬上甲板的我来踢开。",
                "ayuan" => "等一下，看我标哪只。看到记号就开枪。",
                "wenjia" => "它们谁先抬手，我就先瞄谁。",
                _ => "甲板破了你先去补，我这边会接着打。"
            };
            return FindSpecialCompanion(id).displayName + "：" + line;
        }

        private static string CompanionArrivalLine(string id, int day) => (id, day) switch
        {
            ("alo", 1) => "礁那边有只桶逆着水走！捎我一程，我带你去看。",
            ("alo", 2) => "有条鱼游得好好的，突然掉头去扯破网。借个位置，我指给你看。",
            ("alo", 3) => "那黑影撞完堤还往回卷东西。我帮你清甲板，带我去外港。",
            ("ayuan", 1) => "等一下。水在往外走，网却往礁口收。我能搭船再看一眼吗？",
            ("ayuan", 2) => "灯笼鱼折回了三次，都往同一道礁缝照。我想跟船看清它在等什么。",
            ("ayuan", 3) => "近礁有条窄缝，还能走鱼。捎上我吧，过礁时我替你盯着。",
            ("wenjia", 1) => "一只废桶，两次经过同一个浮标。潮没转，我想上船核对漂向。",
            ("wenjia", 2) => "潮标降了半格，礁口的水位却没动。借个位置，我想从船上再量一次。",
            ("wenjia", 3) => "外港中线堵死了，外缘还有水深。借我一程，我带了记录。",
            ("ayu", 1) => "我拉着这根缆，那头却越漂越远。让我跟船找找，回来两个人一起收。",
            ("ayu", 2) => "礁边一整片废网，光靠我捞不动。让我跟船吧，我帮你把还能收的拢起来。",
            _ => "岸上在备回收袋。我会打拖缆结，带我一程，船尾那边交给我。"
        };

        private IEnumerator ShowFinalBriefing()
        {
            var lines = new List<string>
            {
                "兔子船长：巨像会招来海怪。先打退援兵，留意落弹，等它露出核心就集中开火。"
            };
            foreach (SpecialCompanionState state in specialCompanions)
            {
                if (!companionActors.ContainsKey(state.profileId))
                    continue;
                string instruction = state.profileId switch
                {
                    "alo" => "落到脚边的炸弹归我。",
                    "ayuan" => "看见我标的位置，再补一枪。",
                    "wenjia" => "它抬手投东西，我就瞄准。",
                    _ => "你去补船的时候，我来接火。"
                };
                lines.Add(FindSpecialCompanion(state.profileId).displayName + "：" + instruction);
            }
            vessel.Player.BlockMouseAttacks = true;
            yield return ShowStoryConversation(lines.ToArray());
            vessel.Player.BlockMouseAttacks = false;
        }

        private string FinalTowLine()
        {
            SpecialCompanionState speaker = specialCompanions.LastOrDefault(state => companionActors.ContainsKey(state.profileId));
            if (speaker == null)
                return "兔子船长：缆绳吃上力了。贴着灯塔的光走，礁口留给鱼。";
            string line = speaker.profileId switch
            {
                "alo" => "这只桶总算往岸上走了。我去守缆，你开船。",
                "ayuan" => "慢一点。看那道缝，小鱼已经往回游了。",
                "wenjia" => "缆绳绷直了，袋底也没碰礁。就走这条线。",
                _ => "我守船上这一头。靠岸之后，港务接另一头。"
            };
            return FindSpecialCompanion(speaker.profileId).displayName + "：" + line;
        }
    }
}
