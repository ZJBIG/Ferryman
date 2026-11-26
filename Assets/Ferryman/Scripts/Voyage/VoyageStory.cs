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
        private const int DestinationStop = VoyageRouteMap.FinalDepth * 2;
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
                    "码头阿姨：小船长，你的价目牌贴倒了。头回载客，紧张也正常。",
                    "兔子船长：我练了三遍鸣笛，还是把牌贴倒了。幸好备用木板没数错。",
                    "码头阿姨：外海最近常有船带着破网回来。玉米枪保船，萝卜刀清甲板，漏水了就赶紧补。",
                    "兔子船长：日志第一页先写上到站时间。等第一个人平安下船，我再签名。"
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
                    "兔子船长：昨天买船票的孩子还问我，下周能不能坐船去看姥姥。我答应给她留座了。",
                    "守港人：灯塔会备好号角。你先照常送客。两短一长，所有来帮忙的人都知道往哪儿走。"
                },
                (1, 2) => new[]
                {
                    "兔子船长：刚才那只海怪一直挡在礁石前，却没追到码头来。",
                    "码头工：礁缝里全是破渔网，小鱼都过不去了。前阵子有船往那儿倒过东西。",
                    "兔子船长：我们船上可没垃圾要扔……先把挂上来的网带回岸上吧。"
                },
                (1, 6) => new[]
                {
                    "码头工：这行浅印是港口货号。仓库换过货箱，这批旧桶早该送回收场了。",
                    "兔子船长：我在礁缝里见过一样的号码。那只海怪一直挡在船和礁石之间。",
                    "码头工：它从水底往上看，每条船都从这些桶漂来的方向驶进去。换成是我，也会守在礁口。",
                    "兔子船长：那我在日志上记个货号，也记一下它拦船的位置。到终点港翻翻夜间巡检簿。"
                },
                (1, 10) => new[]
                {
                    "港口信使：老灯塔捎来一张纸，昨夜三次撞堤都赶在涨潮头上。第三次后，铁桶自己往外海跑。",
                    "兔子船长：退潮该往另一头走。是什么东西在把桶拽回去？",
                    "港口信使：夜里没看清。你到港后问值班的人，顺便看看堤脚留下的轮胎痕。"
                },
                (2, 6) => new[]
                {
                    "巡海员：炮螺守着产卵礁，水母放冷雾的时候，小鱼正从后面的缝里跑。",
                    "兔子船长：我还以为它们专门等着袭船。原来我们开过去，挡住了鱼群的退路。",
                    "巡海员：记住那道水线。枪口冲着挡路的敌人，船上的破网交给我回收。"
                },
                (2, 10) => new[]
                {
                    "灯塔守望员：你的船靠锚休息时，有条灯笼鱼拖着同伴游过来。它尾巴一直拍同一个地方。",
                    "兔子船长：是网绳卡住了。我趴下去用萝卜刀割断，吓得它一下钻进水里。",
                    "灯塔守望员：它后来又回来了，绕着你捞上来的那只桶照了两圈。",
                    "兔子船长：我把网和桶都留在舱里了。它下回再来，就知道这条船能把东西带走。"
                },
                (3, 6) => new[]
                {
                    "巡海员：产卵礁那边的海怪退了。它们守的那群小鱼，正被黑影卷起的浪往外推。",
                    "兔子船长：只把那堆东西打散，一个潮头过后，岂不是还得漂回礁里？",
                    "巡海员：码头备了粗拖缆和回收袋。记得给船尾腾条路，打散之后还要把东西带回去。"
                },
                (3, 10) => new[]
                {
                    "灯塔守望员：收到信号了。潮闸开着，西侧留了一条活水道，灯笼鱼在那里一直亮着灯。",
                    "兔子船长：上回我顺着它的灯找桶。这次顺着它的灯，把小鱼和废料分开。",
                    deliveredStoryProfiles.Count > 0
                        ? "灯塔守望员：你送到港的朋友已经来领工具了。等乘客都下船，他们会回来帮你。"
                        : "灯塔守望员：港务班把回收袋留给了海鸥。你照顾好甲板，岸上会接住拖缆。"
                },
                (1, DestinationStop) => new[]
                {
                    "守港人：第一趟准时到了。乘客平安，船也还在，这就算开了个好头。",
                    deliveredStoryProfiles.Contains(MonkeyId)
                        ? "枪手猴：我妹说，昨晚的黑影会把废桶吸回身上。撞堤的不是一条鱼，是一整堆会动的垃圾！"
                        : "守港人：灯塔的巡检记录写着，黑影会把废桶吸回身上。撞堤的是一整堆会动的垃圾。",
                    "守港人：这里还留着一排船票。终点港一封，想回家的人就得等潮，绕两座岛。",
                    "兔子船长：日志上先记‘准时到了’。明早空船回来，我想看看那些桶最后漂到哪儿。"
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
            if (StoryRound <= 0 || IsBossLeg || AwaitingStoryDialogue)
                yield break;
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
            BeginStoryConversation(lines);
            // A skip or an encounter can replace this conversation while the timer is waiting.
            string[] conversation = storyDialogueLines;
            while (ActiveRun && conversation == storyDialogueLines)
            {
                int line = storyDialogueIndex;
                float until = Time.time + 3.5f;
                while (ActiveRun && (Time.time < until || Time.timeScale == 0f)
                    && conversation == storyDialogueLines && line == storyDialogueIndex)
                    yield return null;
                if (ActiveRun && conversation == storyDialogueLines && line == storyDialogueIndex)
                    AdvanceStoryDialogue(false);
            }
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
            VoyageAudio.Play(Sound.Gull, 0.55f);
            yield return ShowStoryConversation(
                "兔子船长：黑影散了。灯笼鱼把废料往船尾引，那只寄居蟹在帮它扯开废网。",
                deliveredStoryProfiles.Contains(OwlId)
                    ? "神枪手猫头鹰：冷雾里有水母的蓝光，鱼群都在那边。我的潮线图上标了另一条拖运水道。"
                    : "灯塔守望员：冷雾里有水母的蓝光，鱼群都在那边。你们沿灯塔照的另一道光拖回来。",
                "灯塔守望员：海鸥把回收袋送到船尾了。先收拢大件，碎片让水流带进袋口。",
                deliveredStoryProfiles.Contains(RhinoId)
                    ? "维修师犀牛：袋口扎好了，拖缆挂船尾。我扶着绳，你让船慢慢吃上力。"
                    : "兔子船长：袋口扎好了。把拖缆系在船尾的铁环上，慢慢起步。"
            );
            if (!ActiveRun)
                yield break;

            if (specialCompanions.Count > 0)
                yield return ShowStoryConversation(SpecialCompanionFarewells());
            if (!ActiveRun)
                yield break;

            Message = "沿清出的水道返港";
            yield return Sail(definition.sailingSeconds * 0.5f);
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

            vessel.SetActionsAllowed(true);
            Phase = VoyagePhase.Island;
            Message = "航线重新开放";
            VoyageAudio.Play(Sound.Dock, 0.64f);
            yield return ShowStoryConversation(
                "港务师傅：拖缆接稳了！网、铁桶和塑料分开卸。明天起，返航的船都来这里登记回收。",
                "码头孩子：船长，今天我能坐船去看姥姥了吗？我把那张票留到现在。",
                "兔子船长：能。我们刚从那条水道回来，灯笼鱼一路给船照到了礁口。",
                "兔子船长：来，上船吧。我给你留了靠外的位置。今天这条路好走。"
            );
        }
    }
}
