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
            string reply = StoryRound == 1 ? "兔子船长：上船可以。到了有人要搭船的地方，我还是照样停。"
                : StoryRound == 2 ? "兔子船长：记住你看到的位置。靠岸后，我们去问问管潮闸的人。"
                : "兔子船长：先把乘客送到港。请你留下，今晚我们还要出一趟船。";
            string standby = hasLanding ? null : "兔子船长：先扶住外梯，等甲板空出来我再接你上来。";
            yield return ShowStoryConversation(profile.displayName + "：" + CompanionArrivalLine(id, StoryRound),
                reply, standby, StoryRound == 2 ? CompanionResponse() : null);
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

        private string CompanionResponse()
        {
            SpecialCompanionState first = specialCompanions.FirstOrDefault(state => companionActors.ContainsKey(state.profileId));
            if (first == null)
                return null;
            string response = first.profileId switch
            {
                "alo" => "刀在箱子旁边。我去拿，你指地方。",
                "ayuan" => "你看到鱼群从哪里退走？指给我看，我记下来。",
                "wenjia" => "把位置说准些，离上一个浮标有多远？",
                _ => "我先把湿绳收起来。你站这边，离栏杆远点。"
            };
            return FindSpecialCompanion(first.profileId).displayName + "：" + response;
        }

        private string FirstCompanionCombatLine()
        {
            if (IsBossLeg || specialCompanionOrder.Length == 0
                || eventPlan.Take(StopIndex).Any(plan => plan.kind == VoyageEventKind.Combat))
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
            ("alo", 1) => "我把那批桶推上过栈桥。刚才有只桶自己往上游滚。捎我一段，我认得桶上的号。",
            ("alo", 2) => "刚才那条鱼绕过我，回去扯同伴身上的网。刀借我一下？先让我上来，我指给你看。",
            ("alo", 3) => "那堆东西在吞空桶，桶一贴上去底下就响一声。我能清甲板，带我到外港去看看。",
            ("ayuan", 1) => "等一下。水都在往外走，破网却在往礁里收。我想跟船看看它被什么拽住了。",
            ("ayuan", 2) => "再看一眼，灯笼鱼每次折回去都照着同一条缝。我昨天把这段记成了袭船，让我搭一程，我得把它改过来。",
            ("ayuan", 3) => "闸开以后，碎片都往同一个黑影流。只有近礁那道缝还能走鱼。出港得给它们留着那条路。",
            ("wenjia", 1) => "港口丢了一只回收浮标，我在这里找到了它的底座。准我搭船追一段，我想核对漂来的东西。",
            ("wenjia", 2) => "潮闸底下有网，网结卡在闸轴上。水从别处挤进礁口了，我要看清它冲向哪里。",
            ("wenjia", 3) => "各处的货号能对上了。黑影堵着旧出口，只剩一条窄水道。我带了水深记录，借你的船核一下。",
            ("ayu", 1) => "我守的浮标刚掉进水里，绳子还在往外抽。让我跟船吧，真找着了我来收缆。",
            ("ayu", 2) => "我拉过那根旧缆。拉紧一寸，礁边的鱼就挤得更紧。我跟船去找个能松绳的地方。",
            _ => "我把回收袋的结重打了。外头那团东西要是散开，顺流就能兜住。带我到外港，我替你守后甲板。"
        };

        private IEnumerator ShowSpecialCompanionBriefing()
        {
            if (specialCompanions.Count == 0)
                yield break;
            var lines = new List<string> { "兔子船长：先把水道给鱼留出来。散开的东西，我们自己拖回去。" };
            foreach (SpecialCompanionState state in specialCompanions)
            {
                if (!companionActors.ContainsKey(state.profileId))
                    continue;
                string instruction = state.profileId switch
                {
                    "alo" => "飞上来的东西归我。你看外面。",
                    "ayuan" => "等一下。看见我标的那一处，再开枪。",
                    "wenjia" => "它一抬手，废壳就松。我会在它投东西时先开枪。",
                    _ => "你有空就开枪。要是得补船，我接一会儿。"
                };
                lines.Add(FindSpecialCompanion(state.profileId).displayName + "：" + instruction);
            }
            vessel.Player.BlockMouseAttacks = true;
            yield return ShowStoryConversation(lines.ToArray());
            vessel.Player.BlockMouseAttacks = false;
        }

        private string[] SpecialCompanionFarewells() => specialCompanions.Take(2).Select(state =>
        {
            string line = state.profileId switch
            {
                "alo" => "桶上的号还看得见。我到了岸上去对单。",
                "ayuan" => "再看一眼，那条缝里有鱼出来了。",
                "wenjia" => "这次登记到的是已经收回来的废料。岸上的清单我来签。",
                _ => "这头系好了。靠岸时，让港务接另一头。"
            };
            return FindSpecialCompanion(state.profileId).displayName + "：" + line;
        }).ToArray();
    }
}
