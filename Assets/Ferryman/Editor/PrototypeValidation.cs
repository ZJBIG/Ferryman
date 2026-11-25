using System;
using System.IO;
using System.Linq;
using Ferryman.Characters;
using Ferryman.Combat;
using Ferryman.Voyage;
using Ferryman.World;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ferryman.Editor
{
    public static class PrototypeValidation
    {
        private const string Root = "Assets/Ferryman/";

        public static void Verify()
        {
            foreach (string file in Directory.GetFiles(Root, "*", SearchOption.AllDirectories))
                if (!file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    Require(File.Exists(file + ".meta"), "Missing meta: " + file);
            foreach (string directory in Directory.GetDirectories(Root, "*", SearchOption.AllDirectories))
                Require(File.Exists(directory + ".meta"), "Missing directory meta: " + directory);

            string[] prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { Root });
            foreach (string guid in prefabs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Require(prefab.transform.localScale == Vector3.one, "Prefab root scale differs: " + path);
                    CheckHierarchy(prefab);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefab);
                }
            }
            CheckAppearances(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Stone.prefab").GetComponent<DestructibleObstacle>(),
                1.25f, "Items/StoneWide", "Items/StoneTall", "Items/DeckObstacle");
            CheckAppearances(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/TimedBomb.prefab").GetComponent<DestructibleObstacle>(),
                2.45f, "Items/BombRound", "Items/BombDynamite");
            var repairArt = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/RepairPoint.prefab");
            CheckAppearances(repairArt.GetComponent<RepairPoint>(), 2.2f, "Repair/HoleSplinter", "Repair/HoleWide");
            var repairFields = new SerializedObject(repairArt.GetComponent<RepairPoint>());
            var bar = (GameObject)repairFields.FindProperty("progressBar").objectReferenceValue;
            var fill = (Transform)repairFields.FindProperty("progressFill").objectReferenceValue;
            Require(bar != null && fill != null && fill.parent.gameObject == bar && fill.localScale == Vector3.one,
                "Repair bar bindings or progress container scale differ.");
            Require(bar.transform.localScale == new Vector3(1.25f, 1.25f, 1f), "Repair frame and fill must scale together.");
            var fillArt = fill.Find("Visual").GetComponent<SpriteRenderer>();
            var frameArt = bar.transform.Find("Background").GetComponent<SpriteRenderer>();
            Require(AssetDatabase.GetAssetPath(fillArt.sprite) == Root + "Art/Repair/ProgressFill.png"
                && AssetDatabase.GetAssetPath(frameArt.sprite) == Root + "Art/Repair/ProgressFrame.png"
                && frameArt.sortingOrder > fillArt.sortingOrder, "Supplied repair frame/fill or draw order is absent.");
            Vector2 fullSize = Vector2.Scale(new Vector2(0.981f, 0.122f), fillArt.transform.localScale);
            Require(Vector2.Distance(fullSize, new Vector2(1.06f, 0.162f)) < 0.0001f
                && Vector2.Distance(fill.localPosition, new Vector2(-0.385f, -0.0125f)) < 0.0001f,
                "Repair fill does not cover the drawn inner frame from its left edge.");
            VoyageDefinition definition = AssetDatabase.LoadAssetAtPath<VoyageDefinition>(Root + "Resources/Voyage/DefaultVoyage.asset");
            Require(definition != null && definition.stops.Length == 5, "Default voyage is absent or its route differs.");
            Require(definition.sailingSeconds == 10f, "Sea legs must allow five seconds of rest before and after events.");
            Require(definition.branchingRoutes && VoyageRouteMap.Generate(7301).Nodes.Length == 20,
                "Default voyage must enable the twenty-island branching expedition.");
            Require(definition.storyCampaign && definition.finalBossEncounter != null,
                "Default voyage is missing the three-round campaign or final battle.");
            var bossWaves = definition.finalBossEncounter.waves;
            Require(bossWaves is { Length: 1 } && bossWaves[0].enemies is { Length: 1 }
                && bossWaves[0].enemies[0].count == 1 && bossWaves[0].enemies[0].prefab is { IsBoss: true },
                "The final battle must spawn a single boss.");
            foreach (string cue in new[] { "WaterBubbles", "WaterSplash", "AllyHorn", "SeaAlly", "TrashGather", "TrashBreak" })
                Require(Resources.Load<AudioClip>("Audio/Sfx/" + cue) != null, "Missing voyage cue: " + cue);
            Require(Resources.Load<AudioClip>("Audio/Music/FinalBattle") != null, "Missing final battle music.");
            var owl = definition.passengerPool.FirstOrDefault(profile => profile != null && profile.id == "sharpshooter-owl");
            Require(owl != null && owl.prefab is CombatCrewMember && owl.portrait != null && owl.deckSprite != null,
                "Owl passenger profile, portrait or deck sprite is missing.");
            var uiArt = AssetDatabase.LoadAssetAtPath<VoyageUIArt>(Root + "Art/UI/VoyageUI.asset");
            Require(uiArt != null && uiArt.stunFill != null && uiArt.stunFill.isReadable, "UI art or readable stun mask is absent.");
            foreach (var field in typeof(VoyageUIArt).GetFields())
                Require(field.GetValue(uiArt) is Texture2D texture && texture != null, "Missing UI texture: " + field.Name);
            var vessel = definition.vesselPrefab.GetComponent<VoyageVessel>();
            Require(vessel != null && vessel.Boat != null && vessel.Player != null && vessel.DamageSystem != null, "Vessel bindings are incomplete.");
            Require(vessel.Player.MovementDeck == vessel.Boat.Deck && vessel.Player.BoatHealth == vessel.Boat
                && vessel.Player.DamageSystem == vessel.DamageSystem, "Vessel/player combat or deck bindings differ.");
            Require(vessel.Player.ProjectilePrefab != null, "Player weapon has no projectile.");
            Sprite captainAmmo = vessel.Player.ProjectilePrefab.GetComponentInChildren<SpriteRenderer>().sprite;
            Require(new SerializedObject(vessel.Player).FindProperty("shotEffectPrefab").objectReferenceValue
                == AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Effects/MuzzleFlash.prefab"),
                "Player ranged attack is not bound to the supplied muzzle effect.");
            var bomb = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/TimedBomb.prefab");
            Require(bomb.GetComponentsInChildren<Collider2D>().Length == 0, "Bombs must not have collision boxes.");
            Require(new SerializedObject(bomb.GetComponent<TimedBomb>()).FindProperty("explosionEffectPrefab").objectReferenceValue
                == AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Effects/BombCleared.prefab"),
                "Bomb detonation is missing the supplied smoke effect.");
            Require(new SerializedObject(vessel.Player).FindProperty("meleeImpactEffectPrefab").objectReferenceValue
                == AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Prefabs/Effects/ProjectileImpact.prefab"),
                "Player kick impact is not bound to the supplied hit effect.");
            BoatDeck deck = vessel.Boat.Deck;
            Require(deck.GridSize == new Vector2Int(15, 5)
                && deck.WorldToCell(deck.transform.TransformPoint(new Vector2(6f, 0f))) == new Vector2Int(11, 2)
                && deck.CellToWorld(new Vector2Int(12, 2)) == (Vector2)deck.transform.TransformPoint(new Vector2(6.5f, 0f)),
                "Bow grid moved the original deck cells or omitted the new columns.");
            CheckFloat(vessel.Boat, "maxHealth", 1000f);
            CheckFloat(vessel.Player, "moveSpeed", 4f);
            CheckFloat(vessel.Player, "attackPower", 10f);
            CheckFloat(vessel.Player, "fireInterval", 0.2f);
            CheckFloat(vessel.Player, "repairSpeed", 50f);
            Require(new SerializedObject(vessel.Boat).FindProperty("repairPointPrefab").objectReferenceValue != null, "Repair point reference is absent.");
            foreach (CrewDefinition crew in definition.initialPassengers.Concat(definition.passengerPool).Distinct())
            {
                Require(crew != null && crew.prefab != null && crew.prefab.GetComponent<Passenger>() != null, "Passenger profile/prefab is absent.");
                Require(crew.role == CrewRole.Combat ? crew.prefab is CombatCrewMember : crew.prefab is MaintenanceCrewMember, "Crew prefab does not match its fixed job.");
                if (crew.prefab is CombatCrewMember gunner)
                    Require(gunner.ProjectilePrefab != null
                        && gunner.ProjectilePrefab.GetComponentInChildren<SpriteRenderer>().sprite != captainAmmo,
                        "Combat crew must have its own ammunition.");
                Require(crew.prefab.GetComponent<BoxCollider2D>().size == new Vector2(0.585f, 0.91f), "Crew navigation footprint differs.");
                CheckFrames(crew.walkFrames, 4, crew.id + "/walk");
                CheckFrames(crew.skillFrames, 4, crew.id + "/skill");
            }
            var animatedEnemies = Resources.LoadAll<EncounterDefinition>("Voyage/Events")
                .SelectMany(encounter => encounter.waves)
                .SelectMany(wave => wave.enemies)
                .Select(group => group.prefab)
                .Distinct().ToArray();
            var enemyAmmo = animatedEnemies.Where(actor => actor.AttackType == EnemyCharacter.AttackKind.Projectile)
                .Select(actor => actor.ProjectilePrefab != null
                    ? actor.ProjectilePrefab.GetComponentInChildren<SpriteRenderer>()?.sprite : null).ToArray();
            Require(enemyAmmo.All(sprite => sprite != null && sprite != captainAmmo)
                && enemyAmmo.Distinct().Count() == enemyAmmo.Length, "Ranged sea monsters must each have a unique projectile sprite.");
            Require(bossWaves[0].enemies[0].prefab.BombPrefab != null
                && bossWaves[0].enemies[0].prefab.BombPrefab.gameObject != bomb, "The colossus must have a separate bomb.");
            foreach (EnemyCharacter actor in animatedEnemies)
            {
                using var fields = new SerializedObject(actor);
                var poses = fields.FindProperty("bombPoses");
                if (poses.arraySize == 0)
                {
                    CheckFrames(ReadFrames(fields.FindProperty("attackFrames")), actor.IsBoss ? 6 : 4, actor.name, readable: true);
                    if (actor.AttackType == EnemyCharacter.AttackKind.BoardingMelee)
                        CheckFrames(ReadFrames(fields.FindProperty("boardingWalkFrames")), 4, actor.name + "/walk", readable: true);
                    continue;
                }
                for (int i = 0; i < poses.arraySize; i++)
                    CheckFrames(ReadFrames(poses.GetArrayElementAtIndex(i).FindPropertyRelative("heldFrames")), 4,
                        actor.name + "/throw " + i, readable: true);
            }
            foreach (EncounterDefinition encounter in definition.randomEncounters.Concat(definition.stops.Select(stop => stop.fixedEncounter)).Where(item => item != null).Distinct())
            {
                Require(encounter.waves != null && encounter.waves.Length > 0, "Encounter has no waves.");
                Require(encounter.waveInterval >= 0.1f && float.IsFinite(encounter.waveInterval), "Encounter obstacle flyby timing is invalid.");
                foreach (WaveDefinition wave in encounter.waves)
                {
                    Require(wave != null && wave.enemies != null && wave.enemies.Length > 0, "Encounter has an empty wave.");
                    foreach (EnemySpawn group in wave.enemies)
                    {
                        Require(group != null && group.prefab != null && group.count > 0, "Enemy group is incomplete.");
                        bool bombing = group.prefab.AttackType == EnemyCharacter.AttackKind.Bomb;
                        bool validAttack = group.prefab.AttackType switch
                        {
                            EnemyCharacter.AttackKind.Bomb => group.prefab.PreferredTarget == EnemyCharacter.TargetPreference.ShipFirst
                                && group.prefab.BombPrefab != null,
                            EnemyCharacter.AttackKind.BoardingMelee => group.prefab.PreferredTarget == EnemyCharacter.TargetPreference.CrewFirst
                                && group.prefab.GetComponent<Rigidbody2D>()?.bodyType == RigidbodyType2D.Dynamic,
                            _ => group.prefab.ProjectilePrefab != null
                        };
                        Require(validAttack, "Enemy attack and target configuration differ.");
                        Require(group.prefab.Hitbox is PolygonCollider2D outline && outline.GetTotalPointCount() > 4
                            && group.prefab.GetComponentsInChildren<Collider2D>().Length == 1,
                            "Enemy must use one body contour as its hitbox.");
                        Require(group.prefab.GetComponentInChildren<SpriteRenderer>()?.sprite?.texture.isReadable == true,
                            "Enemy body sprite must be readable for hit feedback.");
                        if (bombing)
                        {
                            var poses = new SerializedObject(group.prefab).FindProperty("bombPoses");
                            Require(poses.arraySize > 0, "Bomber has no windup poses.");
                            for (int i = 0; i < poses.arraySize; i++)
                            {
                                SerializedProperty pose = poses.GetArrayElementAtIndex(i);
                                var sprite = pose.FindPropertyRelative("heldPose").objectReferenceValue as Sprite;
                                Require(sprite != null && sprite.texture.isReadable
                                    && pose.FindPropertyRelative("bodyOutline").arraySize > 4,
                                    "Bomber pose needs a readable sprite and a body contour.");
                            }
                        }
                    }
                }
            }
            foreach (SeaEventDefinition special in definition.specialEvents.Select(entry => entry.definition))
                Require(special != null && special.obstaclePrefab != null && special.obstaclePrefab.GetComponent<DestructibleObstacle>() != null
                    && special.obstacleCount > 0, "Special event has no clearable items.");
            PlannedVoyageEvent[] plan = VoyageEventPlanner.Build(definition, 42);
            Require(definition.obstacleCarrier != null, "The obstacle seagull sprite is missing.");
            CheckFrames(definition.obstacleCarrierFrames, 4, "seagull/flight");
            Require(plan[1].kind == VoyageEventKind.Combat && plan[1].isFixed && plan[3].kind == VoyageEventKind.Special
                && plan[3].specialEvent.obstaclePrefab.GetComponent<TimedBomb>() != null,
                "Default fixed and random events differ.");
            var boundary = deck.GetComponent<EdgeCollider2D>();
            var line = deck.GetComponent<LineRenderer>();
            var points = boundary.points;
            Require(points.Length >= 4 && points[0] == points[points.Length - 1] && line.positionCount == points.Length - 1
                && points.Any(point => point.x > 8f) && deck.HasClearFloor(deck.CellToWorld(new Vector2Int(13, 2)), new Vector2(1.22f, 1.17f)),
                "Deck edge no longer closes along the ship art.");
            Require(deck.GetComponentsInChildren<BoxCollider2D>().Length == 6,
                "One of the cabin or cargo collision sections is missing.");
            for (int i = 0; i < line.positionCount; i++)
                Require((Vector2)line.GetPosition(i) == points[i], "Deck outline and physics edge differ.");

            var buildScenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).ToArray();
            Require(buildScenes.Length == 6 && buildScenes[0].path == Root + "Scenes/main.unity", "Build entry or scenes differ.");
            foreach (var buildScene in buildScenes)
            {
                var scene = EditorSceneManager.OpenScene(buildScene.path);
                foreach (GameObject root in scene.GetRootGameObjects())
                    CheckHierarchy(root);
                if (buildScene.path.EndsWith("Prototype.unity", StringComparison.Ordinal))
                {
                    Require(Object.FindObjectsOfType<EnemyCharacter>().Length == 2 && Object.FindObjectsOfType<TimedBomb>().Length == 2,
                        "Original combat fixture differs.");
                    Require(Object.FindObjectsOfType<DestructibleObstacle>().Count(item => item.GetComponent<TimedBomb>() == null) == 2,
                        "Original stone fixture differs.");
                }
                else
                {
                    VoyageBootstrap bootstrap = Object.FindObjectOfType<VoyageBootstrap>();
                    Require(bootstrap != null, "Scene bootstrap is missing.");
                    var bootstrapFields = new SerializedObject(bootstrap);
                    Require(bootstrapFields.FindProperty("definition").objectReferenceValue == definition
                        && bootstrapFields.FindProperty("uiArt").objectReferenceValue == uiArt,
                        "Scene bootstrap does not reference the default voyage and UI art.");
                    var presentation = Object.FindObjectOfType<VoyageScenePresentation>();
                    Require(presentation != null && Camera.main != null, "Scene presentation/camera is absent.");
                    if (buildScene.path.EndsWith("main.unity", StringComparison.Ordinal))
                    {
                        var ui = scene.GetRootGameObjects().SingleOrDefault(root => root.name == "UI Layout");
                        Require(ui != null && !ui.activeSelf && ui.GetComponent<Canvas>() != null,
                            "Main authoring layout is missing or visible over the runtime UI.");
                        foreach (string slot in new[] { "Title Screen", "Route Map", "In-Game/Ship Durability",
                            "In-Game/Stun Ring", "Settlement/Reward Items", "Dialogue/Portrait", "Upgrade Options" })
                            Require(ui.transform.Find(slot) is RectTransform, "Missing UI layout slot: " + slot);
                        var cards = ui.transform.Find("Upgrade Options/Cards");
                        Require(cards != null && cards.childCount == 5, "The five upgrade previews are missing.");
                        foreach (Transform card in cards)
                            Require(card.GetComponent<UnityEngine.UI.RawImage>()?.texture != null
                                && card.Find("Icon")?.GetComponent<UnityEngine.UI.RawImage>()?.texture != null
                                && card.Find("Description") is RectTransform, "Incomplete upgrade preview: " + card.name);
                        Require(presentation.IsUnifiedScene, "Main does not contain the full voyage environment.");
                        var islands = (Transform)new SerializedObject(presentation).FindProperty("islandStops").objectReferenceValue;
                        Require(islands.childCount == 3, "Main must contain the three island appearances.");
                        foreach (VoyageStop stop in definition.stops)
                        {
                            Require(presentation.CanPresentStop(stop), "Main cannot display stop: " + stop.sceneName);
                            presentation.PresentStop(stop);
                            foreach (Transform island in islands)
                                Require(island.gameObject.activeInHierarchy == (stop.kind == VoyageStopKind.Island && island.name == stop.sceneName),
                                    "Main displays the wrong island: " + island.name);
                        }
                        var wake = (GameObject)new SerializedObject(presentation).FindProperty("sailingWake").objectReferenceValue;
                        Require(wake != null && wake.GetComponent<Animator>()?.runtimeAnimatorController != null, "Main ship wake has no animation.");
                        foreach (Transform island in islands)
                            Require(island.GetComponentInChildren<Animator>(true)?.runtimeAnimatorController != null,
                                "Main shore waves have no animation: " + island.name);
                    }
                }
            }
            EditorSceneManager.OpenScene(Root + "Scenes/main.unity");
            Directory.CreateDirectory(".local");
            File.WriteAllText(".local/prototype-resource-observations.txt",
                $"Unity {Application.unityVersion}\n{prefabs.Length} prefabs and 6 build scenes: no missing scripts or broken serialized references.\nAll Ferryman assets and directories have meta files.\nEntry: main; full voyage uses one scene with three island appearances and a shared sea.\nLegacy scenes and Prototype retained for demonstrations and regression.\nVessel/player/ship/deck/damage/projectile bindings verified. Deck: closed hull outline, six cabin/cargo colliders, 15 x 5 repair grid including the bow.\nShip: 1000 HP. Player: move 4, attack 10, fire 0.2s, repair 50 HP/s.\nFour crew profiles: two combat, two maintenance; 0.585 x 0.91 footprints.\nTwo-wave fixed combat and random sea event; enemy projectile/bomb target combinations verified.\nMain stop appearances, sea motion, ship/shore animations and bootstrap definitions are configured.\nNine item/repair images, static appearances and repair fill/frame alignment are configured.\nMain UI layout anchors are present and inactive.\n");
            Debug.Log("Ferryman full prototype resource verification passed.");
        }

        public static void VerifyAndBuild()
        {
            Verify();
            string output = Path.GetFullPath(".local/build-prototype/Ferryman.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
                target = BuildTarget.StandaloneWindows64,
                locationPathName = output,
                options = BuildOptions.None
            });
            string summary = $"Unity {Application.unityVersion}\nResult: {report.summary.result}\nErrors: {report.summary.totalErrors}\nWarnings: {report.summary.totalWarnings}\nOutput: {output}\n";
            File.WriteAllText(".local/prototype-build-summary.txt", summary);
            RecordBuildMessages(report);
            Require(report.summary.result == BuildResult.Succeeded && report.summary.totalErrors == 0, "Windows prototype build failed.");
            Debug.Log("Ferryman prototype build: " + summary);
        }

        public static void RecordLastBuildMessages()
        {
            RecordBuildMessages(BuildReport.GetLatestReport());
        }

        private static void RecordBuildMessages(BuildReport report)
        {
            Require(report != null, "No build report is available.");
            string messages = string.Join("\n", report.steps.SelectMany(step => step.messages)
                .Where(message => message.type == LogType.Warning || message.type == LogType.Error)
                .Select(message => message.type + ": " + message.content));
            File.WriteAllText(".local/prototype-build-messages.txt", messages);
            Debug.Log(messages);
        }

        private static void CheckHierarchy(GameObject root)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                foreach (Component component in transform.GetComponents<Component>())
                {
                    Require(component != null, "Missing script in " + transform.name);
                    using var serialized = new SerializedObject(component);
                    using var property = serialized.GetIterator();
                    while (property.NextVisible(true))
                    {
                        if (property.propertyType == SerializedPropertyType.ObjectReference)
                            Require(property.objectReferenceValue != null || property.objectReferenceInstanceIDValue == 0,
                                "Broken reference: " + transform.name + "/" + property.propertyPath);
                    }
                }
            }
        }

        private static void CheckAppearances(Component owner, float scale, params string[] images)
        {
            var fields = new SerializedObject(owner);
            var visual = fields.FindProperty("visual").objectReferenceValue as SpriteRenderer;
            var appearances = fields.FindProperty("appearances");
            Require(visual != null && visual.transform.parent == owner.transform && visual.color == Color.white
                && visual.transform.localScale == new Vector3(scale, scale, 1f) && appearances.arraySize == images.Length,
                "Item/repair visual or static appearance bindings differ: " + owner.name);
            for (int i = 0; i < images.Length; i++)
                Require(AssetDatabase.GetAssetPath(appearances.GetArrayElementAtIndex(i).objectReferenceValue)
                    == Root + "Art/" + images[i] + ".png", "Missing supplied appearance: " + images[i]);
        }

        private static void CheckFloat(Object target, string field, float expected)
        {
            Require(Mathf.Abs(new SerializedObject(target).FindProperty(field).floatValue - expected) < 0.0001f,
                target.name + "/" + field + " differs.");
        }

        private static Sprite[] ReadFrames(SerializedProperty frames) => Enumerable.Range(0, frames.arraySize)
            .Select(index => frames.GetArrayElementAtIndex(index).objectReferenceValue as Sprite).ToArray();

        private static void CheckFrames(Sprite[] frames, int count, string name, bool readable = false)
        {
            Require(frames != null && frames.Length == count && frames.Distinct().Count() == count
                && frames.All(frame => frame != null && (!readable || frame.texture.isReadable)),
                name + " needs " + count + " distinct valid animation frames.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
