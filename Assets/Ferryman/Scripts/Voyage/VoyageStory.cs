using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ferryman.Characters;
using UnityEngine;

namespace Ferryman.Voyage
{
    public sealed partial class VoyageController
    {
        public const int FinalStoryRound = 3;
        private const int DestinationStop = VoyageRouteMap.FinalDepth * 2;
        private const int BossStop = DestinationStop + 1;
        private readonly HashSet<string> deliveredStoryProfiles = new HashSet<string>(StringComparer.Ordinal);
        private bool storyPortReady;
        private bool advanceStoryPort;
        private string[] storyDialogueLines = Array.Empty<string>();
        private int storyDialogueIndex;

        public int StoryRound { get; private set; }
        public bool AwaitingStoryDeparture => ActiveRun && Phase == VoyagePhase.Island && storyPortReady && !advanceStoryPort
            && !HasStoryDialogue;
        public bool HasStoryDialogue => ActiveRun && !string.IsNullOrEmpty(StoryFarewell);
        public bool AwaitingStoryDialogue => HasStoryDialogue && storyDialogueLines.Length > 0;
        internal bool IsBossLeg => StoryRound == FinalStoryRound && StopIndex == BossStop;
        private int ExpeditionSeed => VoyageSaveStore.RouteSeed(seed, StoryRound);

        public bool ContinueStoryDialogue()
        {
            if (!AwaitingStoryDialogue)
                return false;
            storyDialogueIndex++;
            if (storyDialogueIndex >= storyDialogueLines.Length)
                ClearStoryDialogue();
            else
                StoryFarewell = storyDialogueLines[storyDialogueIndex];
            VoyageAudio.Play(Sound.Confirm, 0.43f);
            return true;
        }

        private void BeginStoryConversation(params string[] lines)
        {
            storyDialogueLines = lines.Where(line => !string.IsNullOrEmpty(line)).ToArray();
            storyDialogueIndex = 0;
            StoryFarewell = storyDialogueLines.FirstOrDefault();
            if (StoryFarewell != null)
                VoyageAudio.Play(Sound.Dialogue, 0.51f);
        }

        private void ClearStoryDialogue()
        {
            storyDialogueLines = Array.Empty<string>();
            storyDialogueIndex = 0;
            StoryFarewell = null;
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
                    "兔子船长：票价写好了，备用木板也数过了。今天先把第一趟平安跑完。",
                    "码头阿姨：外海这几天不太安生。别光顾着开枪，船漏了就先补，甲板上的炸弹也别留。",
                    "兔子船长：知道啦。乘客是来坐船的，我得把他们好好送到岸上。"
                },
                (2, 0) => new[]
                {
                    "守港人：昨晚灯塔又听到撞堤声。退潮后留下的不是爪印，是轮胎和铁桶刮出的锈痕。",
                    "兔子船长：昨天拦我的海怪，身上也挂着同样的废网。它们到底是在攻击，还是在赶我们离开？",
                    "守港人：先去找修潮闸的犀牛。闸门一开，堵在水底的东西就藏不住了。"
                },
                (3, 0) => new[]
                {
                    "守港人：潮闸修好了，垃圾却一直往同一个黑影里聚。它正堵在终点港外面。",
                    "兔子船长：今天最后一趟，先把大家送到岸上，再去处理它。",
                    "守港人：把一路救过的人记在日志上。他们听见灯塔的号角，就会来帮你。"
                },
                (1, 2) => new[]
                {
                    "兔子船长：刚才那只海怪一直挡在礁石前，却没追到码头来。",
                    "码头工：礁缝里全是破渔网，小鱼都过不去了。前阵子有船往那儿倒过东西。",
                    "兔子船长：我们船上可没垃圾要扔……先把挂上来的网带回岸上吧。"
                },
                (1, 6) => new[]
                {
                    "码头工：你船底刮下来的桶片上有港口货号。昨夜撞堤的东西也带这个号。",
                    "兔子船长：所以它们把我们当成那种船了？难怪连载客的小船也拦。",
                    "码头工：光说不是你没用。把废料带回来，它们看得见。"
                },
                (2, 6) => new[]
                {
                    "巡海员：炮螺守着产卵礁，水母放冷雾的时候，小鱼正从后面的缝里跑。",
                    "兔子船长：我还以为它们专门等着袭船。原来我们开过去，挡住了鱼群的退路。",
                    "巡海员：记住那道水线。枪口冲着挡路的敌人，船上的破网交给我回收。"
                },
                (2, 10) => new[]
                {
                    "守港人：灯塔看见一条灯笼鱼从你船边拉走了同伴，没再往甲板上开火。",
                    "兔子船长：我只是割断了它身上的网。它还照出水底的废桶，让我收走。",
                    "守港人：这比喊一百遍‘我们不是坏船’都管用。再往前，港口就看得见了。"
                },
                (3, 6) => new[]
                {
                    "巡海员：外海的海怪越来越多，海鸥也一趟接一趟地搬东西。你船上还有多少能站脚的地方？",
                    "兔子船长：石头和残骸能踢走，炸弹要先处理。把甲板腾出来，船员才有路去补漏。",
                    "巡海员：等终点港的号角一响，能出海的船都会往你那边赶。撑到港口。"
                },
                (1, DestinationStop) => new[]
                {
                    "守港人：第一趟准时到了。乘客平安，船也还在，这就算开了个好头。",
                    deliveredStoryProfiles.Contains(MonkeyId)
                        ? "枪手猴：我妹说，昨晚的黑影会把废桶吸回身上。撞堤的不是一条鱼，是一整堆会动的垃圾！"
                        : "守港人：灯塔的巡检记录写着，黑影会把废桶吸回身上。撞堤的是一整堆会动的垃圾。",
                    "兔子船长：难怪海怪总从垃圾漂来的方向攻击。明天我想再看看，不能让这条路一直关着。"
                },
                (2, DestinationStop) => new[]
                {
                    deliveredStoryProfiles.Contains(RhinoId)
                        ? "维修师犀牛：潮闸开了！旧网缠了闸轴好几圈，难怪退潮都带不走垃圾。"
                        : "港务师傅：潮闸总算开了。旧网缠了闸轴好几圈，难怪退潮都带不走垃圾。",
                    deliveredStoryProfiles.Contains(OwlId)
                        ? "神枪手猫头鹰：潮线图对上了。废料从各个礁区往终点港外汇，最后全进了那个黑影。"
                        : "守港人：潮线记录对上了。废料从各个礁区往终点港外汇，最后全进了那个黑影。",
                    "兔子船长：我们不是打完一波就没事了。源头还在，明天的船照样会挨打。",
                    "守港人：把船修结实些。最后一趟之后，我们一起把水道清出来。"
                },
                (3, DestinationStop) => new[]
                {
                    "灯塔守望员：急讯！垃圾潮巨像横在出港水道上，堤坝已经撑不住下一次撞击了。",
                    "守港人：乘客都上岸了。现在停航，你还能保住这条船。",
                    "兔子船长：我开的是客运航线，不能只开三天。船能修，水道也得修。给我发号角。",
                    deliveredStoryProfiles.Count > 0
                        ? "灯塔守望员：两短一长！你送过的朋友已经听见了。准备好就离港，我们在岸上接应你。"
                        : "灯塔守望员：两短一长！港务班会接应回收。准备好就离港，别把废料再留在海里。"
                },
                _ => IslandConversation()
            };
            yield return ShowStoryConversation(lines);
        }

        private string[] IslandConversation() => CurrentIsland switch
        {
            VoyageIslandKind.Rest => new[]
            {
                "码头阿姨：船板响成这样还往前赶？先把漏水的地方补好，货棚里还有备用木板。",
                "兔子船长：那就歇一会儿。船员也站稳了再干活，海上可没地方坐下来慢慢修。"
            },
            VoyageIslandKind.Shipyard => new[]
            {
                "船坞师傅：你这船不是旧，是忙。枪口、舱板、维修工具，哪样跟不上，海怪就盯哪样。",
                "兔子船长：给我看看能改哪一项。下段海路更挤，光靠一把玉米枪可顾不过来。"
            },
            VoyageIslandKind.Market => new[]
            {
                "集市老板：船长，今天的木料和弹药都在这儿。海上赚来的船费，花在能救船的地方。",
                "兔子船长：先数数钱。好东西不一定都买得起，我得挑这趟最缺的。"
            },
            VoyageIslandKind.Rescue => new[]
            {
                "灯塔值班员：礁后那条小艇打了求救信号。有人带着枪，有人带着补船工具。",
                "兔子船长：先问问他要去哪儿。愿意搭手就上船，船票可不能换成一张空头承诺。"
            },
            VoyageIslandKind.Treasure => new[]
            {
                "码头孩子：涨潮把这个箱子推上岸了，里面像是船上用的东西。你带它走吧，别再落回海里。",
                "兔子船长：谢谢！能用的装上船，用不了的交回收棚，今天一件也不往水里扔。"
            },
            VoyageIslandKind.Mystery => new[]
            {
                "打捞员：雾里那条货船还压着一舱废料。想捞它得冒险，想改装备也可以来找我。",
                "兔子船长：先看看船还撑不撑得住。赚到东西却沉在回程上，可不算捡了便宜。"
            },
            VoyageIslandKind.Elite => new[]
            {
                "巡海员：你刚穿过的那道封锁，不是单独一只海怪守的。它们开始一起堵船了。",
                "兔子船长：近战的拖船员，远处的轰船板，还有炸弹往脚下落……这次得把岗位搭配好。"
            },
            VoyageIslandKind.Battle => new[]
            {
                "巡海员：气泡一退又会来下一波，别见水面安静就把工具收起来。",
                "兔子船长：记住了。打完先检查甲板，炸弹和漏水都处理好了，才算真的过了这一段。"
            },
            _ => new[]
            {
                "码头工：这边的鱼群已经肯往岸边游了，外海还是乱。带回来的废网放回收棚。",
                "兔子船长：我会继续往前跑。总有一天，上船的人只用担心会不会晕船。"
            }
        };

        private IEnumerator ShowSeaConversation()
        {
            if (StoryRound <= 0 || IsBossLeg || HasStoryDialogue)
                yield break;
            string chatterRunId = runId;
            int chatterStop = StopIndex;
            string[] lines = (StoryRound, (StopIndex / 2) % 3) switch
            {
                (1, 0) when HasStoryCrew(MonkeyId) => new[]
                {
                    "枪手猴：你这支玉米枪挺香啊。先声明，我是看着前头的礁石，没打你枪的主意！",
                    "兔子船长：你要真饿了就说，别拿弹药当午饭。前面还有好几段海路呢。"
                },
                (1, 1) => new[]
                {
                    HasStoryCrew(MonkeyId) ? "枪手猴：我妹说灯塔夜里总听见撞击声。等到了港口，我要亲眼看一看。"
                        : "兔子船长：刚才码头工说，灯塔夜里总听见撞击声。到了港口，我得亲眼看一看。",
                    "兔子船长：先别把所有海怪都当成一伙。刚才那只明明可以追，却又回礁区去了。"
                },
                (2, 0) when HasStoryCrew(RhinoId) && HasStoryCrew(OwlId) => new[]
                {
                    "神枪手猫头鹰：犀牛，能不能轻一点？甲板一抖，我的准星就跟着跳。",
                    "维修师犀牛：我绕远点。你站船头看水线，漏水的地方交给我。"
                },
                (2, 1) => new[]
                {
                    HasStoryCrew(RhinoId) ? "维修师犀牛：修潮闸最怕旧网缠轴。海里那些桶，已经泡了不止一个潮头。"
                        : "兔子船长：这几只废桶上的盐锈比昨天厚。它们在海里，已经泡了不止一个潮头。",
                    "兔子船长：闸门一开，旧东西就会被潮水推出来。到时得查清它们往哪儿走。"
                },
                (2, _) => new[]
                {
                    "兔子船长：刚才那阵冷雾里，小鱼往海怪身后逃。它们好像不只是在追我们。",
                    "兔子船长：把这条水线记下来。先别急着下结论，到了港口找人问问。"
                },
                (3, 0) => new[]
                {
                    "兔子船长：昨天是废桶，今天连断木和旧网都往黑影里去。海上的东西越来越挤了。",
                    "兔子船长：先腾出甲板，照顾好补船的人。到了终点港，我要让这条航路真正开起来。"
                },
                (3, 1) => new[]
                {
                    "兔子船长：日志上已经记了这么多名字。每个人都只想平安到下一座岛。",
                    "兔子船长：等这趟结束，得给码头加个带盖回收箱。水道开了，也不能再让它堵回去。"
                },
                (3, _) => new[]
                {
                    "兔子船长：前面就快看见灯塔了。听见号角先别庆祝，外海那堆东西还在。",
                    "兔子船长：今天赚的船费，先用来补船。最后一段，我想让大家都有回家的路。"
                },
                _ => new[]
                {
                    "兔子船长：那只海鸥又回来了……爪子里可别再是一颗炸弹。",
                    "兔子船长：等下一站靠岸，我得问问这些废桶原来是谁家的。"
                }
            };
            foreach (string line in lines)
            {
                if (!ActiveRun || Phase != VoyagePhase.Sailing || runId != chatterRunId || StopIndex != chatterStop)
                    yield break;
                StoryFarewell = line;
                yield return WaitActive(3.5f);
            }
            if (Phase == VoyagePhase.Sailing && runId == chatterRunId && StopIndex == chatterStop)
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
            if (deliveredStoryProfiles.Count == 0)
                return;
            VoyageAudio.Play(Sound.AllyHorn, 0.74f);
            foreach (string id in deliveredStoryProfiles.OrderBy(id => id, StringComparer.Ordinal))
                SpawnCrew(FindStoryProfile(id), DestinationStop);
            RefreshPassengerConflict();
            VoyageAudio.Play(Sound.SeaAlly, 0.62f, vessel.Boat.Deck.transform.position);
        }
    }
}
