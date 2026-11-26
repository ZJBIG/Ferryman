using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ferryman.Characters;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ferryman.Voyage
{
    public sealed partial class VoyageController
    {
        public const int FinalStoryRound = 3;
        internal const int DestinationStop = VoyageRouteMap.FinalDepth * 2;
        private const int BossStop = DestinationStop + 1;
        private readonly HashSet<string> deliveredStoryProfiles = new HashSet<string>(StringComparer.Ordinal);
        private bool storyPortReady;
        private bool advanceStoryPort;
        private string[] storyDialogueLines = Array.Empty<string>();
        private int storyDialogueIndex;

        public int StoryRound { get; private set; }
        public bool AwaitingStoryDeparture => ActiveRun && Phase == VoyagePhase.Island && storyPortReady && !advanceStoryPort
            && !AwaitingStoryDialogue;
        public bool AwaitingStoryDialogue => ActiveRun && storyDialogueLines.Length > 0;
        public string StoryDialogueLine => AwaitingStoryDialogue ? storyDialogueLines[storyDialogueIndex] : null;
        internal bool IsBossLeg => StoryRound == FinalStoryRound && StopIndex == BossStop;
        private int ExpeditionSeed => VoyageSaveStore.RouteSeed(seed, StoryRound);

        public bool ContinueStoryDialogue() => AdvanceStoryDialogue(true);

        public bool SkipStoryDialogue()
        {
            if (!AwaitingStoryDialogue)
                return false;
            ClearStoryDialogue();
            VoyageAudio.Play(Sound.Confirm, 0.43f);
            return true;
        }

        internal CrewDefinition FindDialogueProfile(string name)
        {
            string id = name switch
            {
                "枪手猴" => MonkeyId,
                "神枪手猫头鹰" => OwlId,
                "捻缝师水獭" => "caulker-otter",
                _ => null
            };
            return definition.passengerPool.Concat(definition.initialPassengers)
                .Concat(definition.specialCompanions ?? Array.Empty<CrewDefinition>())
                .FirstOrDefault(profile => profile != null && (profile.displayName == name
                    || id != null && RelationProfileId(profile.id) == id));
        }

        private bool AdvanceStoryDialogue(bool playSound)
        {
            if (!AwaitingStoryDialogue)
                return false;
            storyDialogueIndex++;
            if (storyDialogueIndex >= storyDialogueLines.Length)
                ClearStoryDialogue();
            if (playSound)
                VoyageAudio.Play(Sound.Confirm, 0.43f);
            return true;
        }

        private void BeginStoryConversation(params string[] lines)
        {
            storyDialogueLines = lines.Where(line => !string.IsNullOrEmpty(line)).ToArray();
            storyDialogueIndex = 0;
            if (storyDialogueLines.Length > 0)
                VoyageAudio.Play(Sound.Dialogue, 0.51f);
        }

        private void ClearStoryDialogue()
        {
            storyDialogueLines = Array.Empty<string>();
            storyDialogueIndex = 0;
        }

        private IEnumerator ShowStoryConversation(params string[] lines)
        {
            BeginStoryConversation(lines);
            while (ActiveRun && AwaitingStoryDialogue)
                yield return null;
        }

        private IEnumerator ShowIslandStory(int index)
        {
            if (StoryRound <= 0)
                yield break;
            string[] lines = (StoryRound, index) switch
            {
                (1, 0) => new[]
                {
                    "码头阿姨：小船长，价目牌贴反啦。头一趟，别慌。",
                    "兔子船长：牌子能重贴，船票上的时间可不能乱改。今天这趟，我要准点到。",
                    "码头阿姨：WASD 或方向键移动。鼠标瞄准，长按左键开火，右键挥刀清障。",
                    "码头阿姨：船漏水时，走到破口附近停下，按住 E 修理。",
                    "码头阿姨：打开海图后，点亮的是下一站能去的岛。选一座，就扬帆出港。"
                },
                (1, 2) => new[]
                {
                    "兔子船长：这附近的海怪常拦船吗？",
                    "码头工：以前这儿的鱼见了船就躲。现在礁口挂满了废网，它们也只剩这一条路。"
                },
                (1, 6) => new[]
                {
                    "码头工：礁边那只锈桶刻着「东仓七号」。我们港的旧桶，早该送去回收了。",
                    "兔子船长：东仓七号。先记在日志上，到了终点港查账。"
                },
                (1, DestinationStop) => new[]
                {
                    deliveredStoryProfiles.Contains(MonkeyId)
                        ? "枪手猴：船长，我妹替你查了！东仓七号是这里的旧货。她昨晚还看见，那团撞堤的黑影把漂走的桶又卷了回去。"
                        : "守港人：账上有东仓七号，是我们港的旧货。昨夜的巡检还记了，那团撞堤的黑影把漂走的桶又卷了回去。",
                    "码头孩子：船长，我想坐船去外岛看姥姥。这张票得等外港开了才能用。",
                    "兔子船长：票收好。等外港开了，我给你留个座。"
                },
                (2, 0) => new[]
                {
                    "守港人：查过潮时了。昨晚明明在退潮，礁口的水却没退。",
                    "兔子船长：那就从潮闸查起。先找找，是哪里堵住了。"
                },
                (2, 6) => new[]
                {
                    "巡海员：礁口的灯笼鱼昨夜又来拦船。船一退开，它们就回头把小鱼往一条窄缝里赶。",
                    "兔子船长：它们守的是那群小鱼。把那条窄缝画给我，下次过礁我绕开。"
                },
                (2, 10) => new[]
                {
                    "灯塔守望员：昨晚有条灯笼鱼被废网缠住。巡船把网捞走后，它跟着船游了一路。",
                    "兔子船长：只割开一个口，它还会再缠上。那张网得带回岸上。"
                },
                (2, DestinationStop) => new[]
                {
                    deliveredStoryProfiles.Contains(RhinoId)
                        ? "维修师犀牛：闸通了。你看这根拆下来的轴，废网在上头绕了三圈。"
                        : "港务师傅：闸通了。废网紧紧缠着闸轴，港务班卸了整根轴才清干净。",
                    deliveredStoryProfiles.Contains(OwlId)
                        ? "神枪手猫头鹰：潮线图上只剩一个汇口。水能过去，漂下来的废料都被黑影截在外港。"
                        : "守港人：潮闸一开，废料全往外港流。灯塔看得清楚，都让那团黑影截住了。",
                    "兔子船长：明天照常跑渡船。等这趟到站，我再出外港，把堵在那儿的东西拖回来。"
                },
                (3, 0) => new[]
                {
                    "守港人：堤上又裂了一道缝。灯塔守着外港，回收的家伙也在备了。",
                    "兔子船长：好。先照常跑这一趟。船票写到终点港，就开到终点港。"
                },
                (3, 6) => new[]
                {
                    "巡海员：近礁的窄水道留给鱼。回收袋和拖缆在港里，打散的废料从外侧拖回来。",
                    "兔子船长：我给船尾留条路。打完就收，趁碎片还没漂回礁口。"
                },
                (3, DestinationStop) => new[]
                {
                    "灯塔守望员：垃圾潮巨像堵在外港。再撞一次，堤就撑不住了。",
                    "守港人：这趟渡船到站了。你要是把船留在港里，也没人怪你。",
                    "兔子船长：我还替人留了个座。今天把路清出来，明天这班船才开得出去。",
                    "灯塔守望员：港里能用金币整备。备好就离港，两短一长是灯塔的信号。"
                },
                _ => Array.Empty<string>()
            };
            if (lines.Length > 0)
                yield return ShowStoryConversation(lines);
        }

        private IEnumerator ShowSeaConversation()
        {
            string line = (StoryRound, StopIndex) switch
            {
                (1, 5) when HasStoryCrew(MonkeyId) =>
                    "枪手猴：我妹总嫌我认错灯塔。船长，真看见了先别喊，我自己认。",
                (1, 9) => "兔子船长：价目牌正了。就是这日志……怎么第一天就湿了半本？",
                (2, 9) => "兔子船长：礁口那条缝也画上吧。日志得擦干一点，别把它洇没了。",
                (3, 11) => "兔子船长：船尾的过道腾出来。平时往岸上送人，今天还要送一袋东西。",
                _ => null
            };
            yield return ShowTimedStoryLine(line);
        }

        private IEnumerator ShowTimedStoryLine(string line)
        {
            if (line == null || AwaitingStoryDialogue)
                yield break;
            BeginStoryConversation(line);
            string[] conversation = storyDialogueLines;
            float until = Time.time + 4f;
            while (ActiveRun && conversation == storyDialogueLines && Time.time < until)
                yield return null;
            if (conversation == storyDialogueLines)
                ClearStoryDialogue();
        }

        private bool HasStoryCrew(string id) => vessel != null && vessel.GetComponentsInChildren<Passenger>()
            .Any(passenger => RelationProfileId(passenger.Definition?.id) == id);

        public bool ContinueStoryVoyage()
        {
            if (!AwaitingStoryDeparture)
                return false;
            advanceStoryPort = true;
            VoyageAudio.Play(Sound.Confirm);
            return true;
        }

        private void LoadStory(int round, VoyageCheckpoint checkpoint = null)
        {
            if (round > 0 && (!definition.branchingRoutes || !definition.storyCampaign || definition.finalBossEncounter == null))
                throw new InvalidDataException("此航程缺少三回剧情或终战配置。");
            StoryRound = round;
            StoryDockOfferResolved = checkpoint != null && checkpoint.storyDockOfferResolved;
            ClearStoryDialogue();
            storyPortReady = false;
            advanceStoryPort = false;
            deliveredStoryProfiles.Clear();
            foreach (string id in checkpoint?.deliveredStoryProfiles ?? Array.Empty<string>())
            {
                var profile = FindStoryProfile(id);
                if (round == 0 || profile == null)
                    throw new InvalidDataException("检查点包含未知的终战援军。");
                deliveredStoryProfiles.Add(profile.id);
            }
        }

        private void AdvanceToNextRound()
        {
            int[] upgrades = runUpgrades;
            StoryRound++;
            InitializeExpedition(true, null);
            runUpgrades = upgrades;
            StoryDockOfferResolved = false;
            StopIndex = 0;
            SaveCheckpoint();
        }

        private void BoardFinalAllies()
        {
            VoyageAudio.Play(Sound.AllyHorn, 0.74f);
            if (specialCompanionOrder.Length > 0)
            {
                foreach (CrewDefinition profile in definition.specialCompanions)
                {
                    SpecialCompanionState state = specialCompanions.FirstOrDefault(item => item.profileId == profile.id);
                    if (state == null)
                    {
                        state = new SpecialCompanionState { profileId = profile.id };
                        specialCompanions.Add(state);
                    }
                    if (!companionActors.ContainsKey(profile.id))
                        TryPlaceCompanion(state);
                }
            }
            if (deliveredStoryProfiles.Count == 0)
                return;
            foreach (string id in deliveredStoryProfiles.OrderBy(id => id, StringComparer.Ordinal))
                waitingFinalAllies.Add(FindStoryProfile(id));
            PlaceFinalAllies();
            VoyageAudio.Play(Sound.SeaAlly, 0.62f, vessel.Boat.Deck.transform.position);
        }

        private IEnumerator ShowFinalReturn()
        {
            // The saved stop remains the boss entrance until settlement succeeds.
            waitingFinalAllies.Clear();
            CleanupHazards(true);
            vessel.Player.BlockMouseAttacks = true;
            Phase = VoyagePhase.Sailing;
            Message = "回收水道里的残骸";
            yield return ShowStoryConversation(
                "兔子船长：东仓七号。绕了三天，原来还在这儿。",
                "兔子船长：袋子就要到了。到船尾收缆，把散在水里的都带回去。");
            if (!ActiveRun)
                yield break;

            yield return RecoverFinalWreckage();
            if (!ActiveRun)
                yield break;

            Message = "沿清出的水道返港";
            StartFinalTow();
            StartCoroutine(ShowTimedStoryLine(FinalTowLine()));
            yield return Sail(definition.sailingSeconds * 0.5f);
            ClearStoryDialogue();
            vessel.SetActionsAllowed(false, true);
            Phase = VoyagePhase.Leaving;
            yield return AnimateExit();
            if (!ActiveRun)
                yield break;

            Phase = VoyagePhase.Transition;
            StopIndex = DestinationStop;
            VoyageStop port = ActiveStops[DestinationStop];
            if (!TryPresentStop(port))
                yield return SceneManager.LoadSceneAsync(port.sceneName);
            Phase = VoyagePhase.Entering;
            yield return AnimateEntry();
            if (!ActiveRun)
                yield break;

            DockFinalTow();
            vessel.SetActionsAllowed(true);
            Phase = VoyagePhase.Island;
            Message = "航线重新开放";
            VoyageAudio.Play(Sound.Dock, 0.64f);
            yield return ShowStoryConversation(
                "港务师傅：缆接住了。东仓七号，销账。往后船一靠岸，废网、空桶都在这儿交。",
                "码头孩子：船长，我的票还在。现在能去看姥姥了吗？",
                "兔子船长：明早第一班，你的座位还留着。过了灯塔，我指给你看鱼回家的路。"
            );
        }
    }
}
