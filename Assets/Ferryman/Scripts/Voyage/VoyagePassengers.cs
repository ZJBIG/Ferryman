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
        private static readonly string[][] CaptainEncounterLines =
        {
            new[]
            {
                "前面的水在冒泡。先听我鸣笛，要是它还挡着船，再开枪。",
                "礁边那几条鱼掉头了。大家站稳，先看清是从哪边来的。",
                "又是从碎网那边过来的。枪口抬高些，别打到水面上的小鱼。",
                "海鸥刚往下看了一眼。注意脚下，甲板上落东西就给我让条路。",
                "右舷的水推得不一样。看好那个方向，我守住船头。",
                "前头还有人在等船。分开站，一人盯一边。",
                "灯塔已经看得见了。守住最后这段，先把乘客送上岸。"
            },
            new[]
            {
                "泡从旧网下面冒出来了。让它离开礁口，再迎上去。",
                "那个方向是潮闸。避开漂来的桶，看准海怪再打。",
                "小鱼先往礁缝里走。维持航向，给它们留地方。",
                "留意冰蓝色的弹。要是手脚慢下来，就把近处的破口交给搭档。",
                "船舷有砂粒。能修的人靠里面站，别都挤在上风口。",
                "这段的水比刚才浑。看见冒泡的地方就报一声。",
                "港口的闸轴还等着人去修。守住船，别追离航线的海怪。"
            },
            new[]
            {
                "今天海上漂的东西格外多。动起来，别让它们把路堵死。",
                "灯笼鱼贴着礁石走。给它留出口，盯住挡在正前方的敌人。",
                "甲板上腾出一块空地。落下来的东西我来清，你们守舷边。",
                "外海的水开始往回拽。炮手守前面，补板的人留在船中。",
                "这群是被卷过来的。只打还在袭船的，别把鱼群逼回去。",
                "堤上的旗还没降。再护住这一程，岸上的人就能平安下船。",
                "快到港了。把找回来的漂浮物留在船里，下船再交回收棚。"
            }
        };

        private static readonly Dictionary<string, string[]> CrewEncounterLines = new Dictionary<string, string[]>
        {
            [MonkeyId] = new[]
            {
                "头一回在船上碰见这个阵仗……我守上舷，船长你看右边！",
                "我听见水底在响，枪口已经对过去了。你喊一声我就打。",
                "灯塔值夜也得听这种动静吧？到了我得给妹妹带副耳塞。",
                "我记住了它们浮上来的样子，先忍一拍，等脑袋露全再打。",
                "刚补完弹丸，一粒也没受潮。这回我把船头的空地留给你。",
                "我认得这道礁。过了它就能看见灯塔，不用扯着嗓子喊，给个手势就够了。"
            },
            [RhinoId] = new[]
            {
                "船长，我把补板靠在舱门了。头一块裂了，叫我就行。",
                "这回我先摸了船板，旧补丁都结实。要漏水我能听见。",
                "靠外的两块板还空着，交给我。你别让废网钩住船。",
                "这跟拆潮闸也差不多，先撑住受力的地方，再把缝堵实。",
                "先别挪那叠木料。我按长度码好了，破哪个位置都能找到合用的一块。",
                "前半段用掉了不少铆钉，舱底还有一盒。打完我再去拿。"
            },
            [OwlId] = new[]
            {
                "第一串泡在斜前方。我看水线，等它露头再开枪。",
                "刚才水线往船尾压。等我站稳，第一枪从另一边过去。",
                "风改了方向。别追飘过去的弹，看水纹就知道它在哪儿。",
                "我把灯照到外侧了。那一线的鱼正往下游，让它们走。",
                "左边是浪花，右边才是气泡。准备往右转，我跟着你的船头走。",
                "云遮住了岛上的标灯。等它露出来，我就知道正对着哪个礁口。"
            },
            ["cannoneer-boar"] = new[]
            {
                "我这回算是头一次给渡船护航。开炮前会喊，捂好耳朵！",
                "听见响声别回头，我守的是那片冒泡的水。",
                "有船板要换就从我后面走。我打完这一发给你让路。",
                "离得远就先等一等，我可不想把炮打在岸上的回收桶上。",
                "炮架给浪晃松了。我已经拧牢，这回开炮甲板不会跟着跳。",
                "前面是浅水，别往礁石里追。把它们赶开，我就收炮。"
            },
            ["carpenter-beaver"] = new[]
            {
                "我刚上船就轮到海怪来验榫？站稳，我把扳手收好了。",
                "补过的地方不会先坏。等我看看这次哪块板响。",
                "我要守船肋那边。你看见弹压下来，就把我叫开。",
                "上次用掉一根短木，这次我换长的，能撑住两边的榫。",
                "我把钉盒盖上了。这阵浪过去，甲板上就少一样东西可滚。",
                "上一处补板留着呢，等靠港再磨平。先别踩着那根翘起来的钉子。"
            },
            ["caulker-otter"] = new[]
            {
                "这是我搭这条船遇到的第一次。船长，帮我留一条到船舷的路。",
                "麻绳这回泡透了，只要听见漏水声，我马上去封缝。",
                "有东西滚过来就喊我，我手里拿着填料看不见脚边。",
                "你护住外侧就好，我从舱门绕过去，干完这边再换人。",
                "这回我把填料分到两边了，从哪边开始漏就往哪边去。",
                "放心，旧缝早封严了。现在船板响的是外侧，别往舱底跑。"
            }
        };

        private IEnumerator ShowEncounterConversation()
        {
            var aboard = vessel.GetComponentsInChildren<Passenger>().Where(passenger => passenger.Definition != null).ToArray();
            int leg = Mathf.Max(0, StopIndex / 2);
            int day = Mathf.Clamp(StoryRound, 1, FinalStoryRound);
            var lines = CaptainEncounterLines[day - 1];
            string captain = "兔子船长：" + (IsBossLeg
                ? "两短一长，灯塔在给我们指路。等那堆废料浮起来，守住船，别让它再堵水道。"
                : lines[leg % lines.Length]);
            string companionLine = FirstCompanionCombatLine();
            if (aboard.Length == 0)
            {
                yield return ShowStoryConversation(companionLine, captain);
                yield break;
            }

            // Give new passengers the first word. Later encounters rotate through the crew.
            Passenger speaker = aboard.FirstOrDefault(passenger => passenger.EncountersAboard == 0)
                ?? aboard[leg % aboard.Length];
            int encounters = speaker.EncountersAboard;
            CrewDefinition profile = speaker.Definition;
            string id = RelationProfileId(profile.id);
            string reaction;
            if (IsBossLeg)
                reaction = id switch
                {
                    RhinoId => "闸门那边已经开好了。甲板有破口我先补，打完我去接拖缆。",
                    OwlId => "右边是鱼群的退路。我盯着聚起来的废料，船长，你让开那道水线。",
                    _ => "灯塔那头有我妹守着。船长，咱们把挡在水道上的那一堆清干净！"
                };
            else if (CrewEncounterLines.TryGetValue(id, out string[] reactions))
                reaction = reactions[encounters == 0 ? 0 : 1 + (encounters - 1) % (reactions.Length - 1)];
            else if (profile.role == CrewRole.Combat)
                reaction = encounters == 0 ? "我头一次在这条船上碰见海怪。船长，枪口交给我。"
                    : "我站在上舷，你盯另一边。我们还是按上次的分工来。";
            else
                reaction = encounters == 0 ? "船长，我刚上船就赶上这阵泡。甲板我来守。"
                    : "上回剩的补板还在，我就在工具箱旁边。";

            foreach (Passenger passenger in aboard)
                passenger.EncountersAboard = Mathf.Min(passenger.EncountersAboard, 999) + 1;
            yield return ShowStoryConversation(profile.displayName + "：" + reaction, companionLine, captain);
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
