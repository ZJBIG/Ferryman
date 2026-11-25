using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ferryman.Characters;
using UnityEngine;
using Random = System.Random;

namespace Ferryman.Voyage
{
    public sealed partial class VoyageController
    {
        private const string MonkeyId = "gunner-monkey";
        private const string RhinoId = "mechanic-rhino";
        private const string OwlId = "sharpshooter-owl";

        private int storyBoardingStop;
        private bool storyRescueOffer;
        private CrewDefinition[] alarmProfiles = System.Array.Empty<CrewDefinition>();
        private float alarmStartedAt;
        private const float AlarmLineSeconds = 4f;

        public CrewDefinition AlarmProfile
        {
            get
            {
                if (!ActiveRun || Phase != VoyagePhase.Event)
                    return null;
                int index = Mathf.FloorToInt((Time.time - alarmStartedAt) / AlarmLineSeconds);
                return index >= 0 && index < alarmProfiles.Length ? alarmProfiles[index] : null;
            }
        }
        public string AlarmDialogue => AlarmProfile == null ? null : AlarmProfile.id switch
        {
            MonkeyId or "gunner" => "哎哎，怎么突然打起来了？我们只是送客，又没抢它们的鱼！船长，先护住船！",
            RhinoId or "mechanic" => "这一下真够重的。我们好端端赶路，怎么就挨打了？你看前头，我先把船补好。",
            OwlId => "它们的攻击有方向……可我们只是经过，为什么把我们当敌人？先躲开，我再看清楚。",
            "cannoneer-boar" or "heavy-gunner" => "谁在朝咱们开火？过个海也要打架？来，躲我后头，让我顶住！",
            "carpenter-beaver" or "engineer" => "哎呀，刚钉好的木板！到底出了什么事？我们又没拆它们的窝，我先把这儿加固。",
            "caulker-otter" => "船长，先别慌！我们明明只是载客，它们为什么追着打？你掌舵，我去堵住漏水的地方。",
            _ => AlarmProfile.role == CrewRole.Combat
                ? "怎么突然朝我们开火？我们只是载客，哪里惹到它们了？船长，我来掩护！"
                : "到底出了什么事？我们只是赶路，为什么会被攻击？先把船补好再说！"
        };

        private void BeginPassengerAlarm()
        {
            ClearStoryDialogue();
            alarmProfiles = vessel.GetComponentsInChildren<Passenger>()
                .Select(passenger => passenger.Definition).Where(profile => profile != null).ToArray();
            alarmStartedAt = Time.time;
            if (alarmProfiles.Length > 0)
                VoyageAudio.Play(Sound.Dialogue, 0.51f);
        }

        public bool BoardingIsChoice => StoryRound > 0 && BoardingProfile != null;
        public string BoardingOrigin => BoardingIsChoice ? ActiveStops[StopIndex].displayName : null;
        public string BoardingDialogue => BoardingIsChoice ? DescribeRequest(BoardingProfile.id) : null;
        public string BoardingConflict => RelationProfileId(BoardingProfile?.id) switch
        {
            MonkeyId => "“我和野猪爱较劲：他打得更重，我开火更快。”",
            RhinoId => "“和海狸搭档，我俩维修都快些；猫头鹰怕我的脚步。”",
            OwlId => "“我怕犀牛的重脚步，离得太近会先停火躲开。”",
            "cannoneer-boar" => "“猴子能激起我的斗志，不过水獭听见炮声会分心。”",
            "carpenter-beaver" => "“水獭封缝，犀牛扶板，有他们搭手，我修得更快。”",
            "caulker-otter" => "“和海狸搭档我修得更快；野猪的炮声会让我分心。”",
            _ => null
        };
        public string StoryFarewell { get; private set; }
        private bool StoryDockOfferResolved { get; set; }
        private IEnumerable<CrewDefinition> StoryRescueProfiles =>
            definition.passengerPool.Where(profile => !IsStoryProfile(profile.id) && !HasPassenger(profile));

        private static bool IsStoryProfile(string id) => id is MonkeyId or RhinoId or OwlId;

        private CrewDefinition FindStoryProfile(string id)
        {
            if (StoryRound > 0 && id == "lookout-rabbit")
                id = OwlId;
            if (!IsStoryProfile(id))
                return null;
            return definition.passengerPool.FirstOrDefault(profile => profile.id == id)
                ?? Resources.Load<CrewDefinition>("Voyage/Crew/" + id);
        }

        private IEnumerator OfferStoryPassengerAtIsland(VoyageStop stop, int index)
        {
            if (index <= 0 || index >= DestinationStop || StoryDockOfferResolved)
                yield break;

            string scheduled = (StoryRound, index) switch
            {
                (1, 2) => MonkeyId,
                (2, 2) => RhinoId,
                (2, 4) => OwlId,
                _ => null
            };
            if (scheduled != null)
            {
                CrewDefinition profile = FindStoryProfile(scheduled);
                if (profile == null)
                    throw new InvalidDataException("缺少剧情乘客配置：" + scheduled);
                OpenBoardingOffer(profile, DestinationStop, false);
            }
            else
            {
                var random = new Random(ExpeditionSeed ^ index * 9973);
                if (random.NextDouble() < stop.boardingChance)
                {
                    CrewDefinition[] available = StoryRescueProfiles.ToArray();
                    if (available.Length > 0)
                    {
                        CrewDefinition profile = available[random.Next(available.Length)];
                        int destination = 2 * random.Next(index / 2 + 1, VoyageRouteMap.FinalDepth + 1);
                        OpenBoardingOffer(profile, destination, false);
                    }
                }
            }

            while (ActiveRun && (BoardingProfile != null || AwaitingStoryDialogue))
                yield return null;
            StoryDockOfferResolved = true;
        }

        private bool OfferStoryRescue(CrewRole role)
        {
            CrewDefinition profile = StoryRescueProfiles.FirstOrDefault(item => item.role == role);
            if (profile == null)
                return false;
            OpenBoardingOffer(profile, Mathf.Min(DestinationStop, StopIndex + 4), true);
            return true;
        }

        private void OpenBoardingOffer(CrewDefinition profile, int destination, bool rescue)
        {
            BoardingProfile = profile;
            storyBoardingStop = destination;
            storyRescueOffer = rescue;
            boardingDestination = destination == DestinationStop
                ? "终点港" : $"第 {destination / 2} 层起的首座可下客岛";
            VoyageAudio.Play(Sound.Dialogue, 0.51f);
        }

        public bool ResolveBoardingOffer(bool accept)
        {
            if (!ActiveRun || Phase != VoyagePhase.Island || !BoardingIsChoice)
                return false;

            CrewDefinition profile = BoardingProfile;
            if (accept && BoardPassenger(profile, storyBoardingStop) == null)
                return false;
            if (storyRescueOffer)
                IslandResolved = true;
            else
                StoryDockOfferResolved = true;
            BoardingProfile = null;
            storyRescueOffer = false;
            storyBoardingStop = 0;
            if (accept)
                BeginStoryConversation(BoardingExchange(profile));
            else
                BeginStoryConversation(profile.displayName + "：那我再等等下一条船。前面的水道不太平，船长保重。");
            SaveCheckpoint();
            VoyageAudio.Play(Sound.Confirm);
            return true;
        }

        private IEnumerator ShowStoryFarewell(CrewDefinition profile)
        {
            string[] lines = RelationProfileId(profile.id) switch
            {
                MonkeyId => new[]
                {
                    "枪手猴：谢啦，船长！我先去老灯塔接我妹的班，她已经一晚上没合眼了。",
                    "兔子船长：让她好好歇着。你看见黑影往哪儿去，就帮我记下来，下回我们对对日志。"
                },
                RhinoId => new[]
                {
                    "维修师犀牛：工具没少一件，先去换闸轴。你们听到潮水重新流起来，就知道我修好了。",
                    "兔子船长：甲板的木板多亏你撑住。港口要是缺拖缆，等我下一趟带来。"
                },
                OwlId => new[]
                {
                    "神枪手猫头鹰：潮线图没湿，我这就送上灯塔。还有，替我谢谢犀牛，他最后几段一直绕着我走。",
                    "兔子船长：你们一个看前路，一个顾船板，都少不了。等灯塔有新消息，记得叫我。"
                },
                "cannoneer-boar" => new[]
                {
                    "炮手野猪：到了！我先去找木料。下回再碰见那群海怪，给我留个开炮的位置。",
                    "兔子船长：位置会留，耳朵也得留。你下次先提醒水獭一声，再把炮打响。"
                },
                "carpenter-beaver" => new[]
                {
                    "木匠海狸：榫头还好好的，渔船今晚就能换上桅杆。你船上那几块补板也够结实。",
                    "兔子船长：有你们轮着搭手，旧船才没散。等那边忙完，再来看看我的甲板。"
                },
                "caulker-otter" => new[]
                {
                    "捻缝师水獭：麻绳没沾盐水，这趟送得正好。船长，底舱的缝我也都封过了。",
                    "兔子船长：知道啦。下次让海狸再检查一遍，你们俩搭档，我就放心多了。"
                },
                _ => new[] { profile.displayName + "：多谢船长，平安到站了。前面的岛要是有人等船，我会告诉他你的航线。" }
            };
            yield return ShowStoryConversation(lines);
        }

        private string[] BoardingExchange(CrewDefinition profile)
        {
            string id = RelationProfileId(profile.id);
            var lines = new List<string>
            {
                id switch
                {
                    MonkeyId => "枪手猴：那我坐船头！到灯塔之前，我替你看着那些突然冒泡的家伙。",
                    RhinoId => "维修师犀牛：我站靠舱门这边。板子一裂就叫我，别等水漫到脚底才说。",
                    OwlId => "神枪手猫头鹰：我想站稳一点。先看水线，再开枪，准星不能跟着别人的脚步跳。",
                    "cannoneer-boar" => "炮手野猪：枪膛备好了！谁拦这条船，就让它先听听我的动静。",
                    "carpenter-beaver" => "木匠海狸：甲板的榫头松了两处，到了海上我顺手修。谁扶板子，谁帮我封缝？",
                    "caulker-otter" => "捻缝师水獭：我带了麻绳和填料。别拿重东西压住过道，漏水的时候我得能跑过去。",
                    _ => profile.displayName + "：我上船啦。到了目的地之前，岗位上的事交给我。"
                },
                "兔子船长：站稳了。我们船上没有闲人，也没有谁能一个人顾住所有地方。"
            };
            if ((id == MonkeyId || id == "cannoneer-boar") && HasStoryCrew(MonkeyId) && HasStoryCrew("cannoneer-boar"))
            {
                lines.Add("枪手猴：野猪，敢不敢比谁先打退下一只？我这边可要加快开火了！");
                lines.Add("炮手野猪：比就比！你忙着连射，我把每发打得更重，谁也别偷懒。");
            }
            if ((id == "carpenter-beaver" || id == "caulker-otter")
                && HasStoryCrew("carpenter-beaver") && HasStoryCrew("caulker-otter"))
            {
                lines.Add("木匠海狸：我把榫头钉紧，你把缝封严。咱俩一起修，比各忙各的快三成。");
                lines.Add("捻缝师水獭：这才是好搭档！你留下的板缝，我一眼就找得到。");
            }
            if ((id == RhinoId || id == "carpenter-beaver") && HasStoryCrew(RhinoId) && HasStoryCrew("carpenter-beaver"))
            {
                lines.Add("维修师犀牛：大板子我来扶，你只管敲榫。咱们搭手，维修能再快两成。");
                lines.Add("木匠海狸：说定了！你别跟着我的锤子晃，船底这次一定补结实。");
            }
            if ((id == "cannoneer-boar" || id == "caulker-otter")
                && HasStoryCrew("cannoneer-boar") && HasStoryCrew("caulker-otter"))
            {
                lines.Add("捻缝师水獭：你那炮声一响，我手里的绳结就打错。修船慢了两成，可别怪我！");
                lines.Add("炮手野猪：……下次我先喊一声。船长，你得把别的补船搭档也安排好。");
            }
            if ((id == OwlId || id == RhinoId) && HasStoryCrew(OwlId) && HasStoryCrew(RhinoId))
            {
                lines.Add("神枪手猫头鹰：犀牛，别靠得太近。你的脚步一重，我得先停火绕到另一边。");
                lines.Add("维修师犀牛：好，我走舱门那边。你看远处的敌人，我看脚下的漏水。");
            }
            return lines.ToArray();
        }

        private string DescribeRequest(string id)
        {
            return id switch
            {
                MonkeyId => "我妹妹在灯塔等我。船长，能捎我一程吗？",
                RhinoId => "潮闸坏了，我得赶在涨潮前过去看看。",
                OwlId => "我得把这几晚画的潮线图送到灯塔。",
                "cannoneer-boar" => "暴风吹断了家里的码头，我要去找木料。海怪敢来，我的枪管还热着呢。",
                "carpenter-beaver" => "隔壁岛的渔船断了桅杆。人还等着出海，我得赶在天黑前把榫头装好。",
                "caulker-otter" => "涨潮淹了好几间仓库。我想把防水的麻绳送过去，路上也能替你补漏。",
                _ => "船长，能捎我一段吗？我有东西要送到前面的岛，路上可以帮着照看船。"
            };
        }

        private void RefreshPassengerConflict()
        {
            if (vessel == null)
                return;

            Passenger[] aboard = vessel.GetComponentsInChildren<Passenger>();
            var profiles = new HashSet<string>(aboard.Select(member => RelationProfileId(member.Definition?.id))
                .Where(id => id != null), System.StringComparer.Ordinal);
            Passenger owl = null;
            Passenger rhino = null;
            foreach (Passenger member in aboard)
            {
                string id = RelationProfileId(member.Definition?.id);
                if (member.Actor is CombatCrewMember combat)
                    combat.RefreshCrewRelations(id, profiles);
                else if (member.Actor is MaintenanceCrewMember maintenance)
                    maintenance.RefreshCrewRelations(id, profiles);
                if (id == OwlId)
                    owl = member;
                else if (id == RhinoId)
                    rhino = member;
            }
            if (owl != null && owl.Actor is CombatCrewMember lookout)
                lookout.SetAvoidedCrew(StoryRound <= 0 || rhino == null ? null : rhino.Actor);
        }

        private static string RelationProfileId(string id) => id switch
        {
            "gunner" => MonkeyId,
            "heavy-gunner" => "cannoneer-boar",
            "engineer" => "carpenter-beaver",
            "mechanic" => RhinoId,
            _ => id
        };
    }
}
