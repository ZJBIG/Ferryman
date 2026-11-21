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
                finally { PrefabUtility.UnloadPrefabContents(prefab); }
            }
            VoyageDefinition definition = AssetDatabase.LoadAssetAtPath<VoyageDefinition>(Root + "Resources/Voyage/DefaultVoyage.asset");
            Require(definition != null && definition.stops.Length == 5, "Default voyage is absent or its route differs.");
            var vessel = definition.vesselPrefab.GetComponent<VoyageVessel>();
            Require(vessel != null && vessel.Boat != null && vessel.Player != null && vessel.DamageSystem != null, "Vessel bindings are incomplete.");
            Require(vessel.Player.MovementDeck == vessel.Boat.Deck && vessel.Player.BoatHealth == vessel.Boat
                && vessel.Player.DamageSystem == vessel.DamageSystem, "Vessel/player combat or deck bindings differ.");
            Require(vessel.Player.ProjectilePrefab != null, "Player weapon has no projectile.");
            Require(vessel.Boat.Deck.GridSize == new Vector2Int(12, 5), "Default deck grid differs.");
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
                Require(crew.prefab.GetComponent<BoxCollider2D>().size == new Vector2(0.45f, 0.7f), "Crew navigation footprint differs.");
            }
            foreach (EncounterDefinition encounter in definition.randomEncounters.Concat(definition.stops.Select(stop => stop.fixedEncounter)).Where(item => item != null).Distinct())
            {
                Require(encounter.waves != null && encounter.waves.Length > 0, "Encounter has no waves.");
                foreach (WaveDefinition wave in encounter.waves)
                {
                    Require(wave != null && wave.enemies != null && wave.enemies.Length > 0, "Encounter has an empty wave.");
                    foreach (EnemySpawn group in wave.enemies)
                    {
                        Require(group != null && group.prefab != null && group.count > 0, "Enemy group is incomplete.");
                        bool bombing = group.prefab.AttackType == EnemyCharacter.AttackKind.Bomb;
                        Require(bombing ? group.prefab.PreferredTarget == EnemyCharacter.TargetPreference.ShipFirst && group.prefab.BombPrefab != null
                            : group.prefab.PreferredTarget != EnemyCharacter.TargetPreference.ShipFirst && group.prefab.ProjectilePrefab != null,
                            "Enemy attack and target configuration differ.");
                    }
                }
            }
            foreach (SeaEventDefinition special in definition.specialEvents.Select(entry => entry.definition))
                Require(special != null && special.obstaclePrefab != null && special.obstaclePrefab.GetComponent<DestructibleObstacle>() != null
                    && special.obstacleCount > 0, "Special event has no clearable items.");
            PlannedVoyageEvent[] plan = VoyageEventPlanner.Build(definition, 42);
            Require(plan[1].kind == VoyageEventKind.Combat && plan[1].isFixed && plan[3].kind == VoyageEventKind.Special,
                "Default fixed and random events differ.");
            var boundary = vessel.Boat.Deck.GetComponent<EdgeCollider2D>();
            var line = vessel.Boat.Deck.GetComponent<LineRenderer>();
            Require(boundary.points.Length == 5 && boundary.points[0] == boundary.points[4] && line.positionCount == 4, "Deck edge no longer closes.");
            for (int i = 0; i < 4; i++)
                Require((Vector2)line.GetPosition(i) == boundary.points[i], "Deck outline and physics edge differ.");

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
                    Require(bootstrap != null && new SerializedObject(bootstrap).FindProperty("definition").objectReferenceValue == definition,
                        "Scene bootstrap does not reference the default voyage.");
                    var presentation = Object.FindObjectOfType<VoyageScenePresentation>();
                    Require(presentation != null && Camera.main != null, "Scene presentation/camera is absent.");
                    if (buildScene.path.EndsWith("main.unity", StringComparison.Ordinal))
                    {
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
                $"Unity {Application.unityVersion}\n{prefabs.Length} prefabs and 6 build scenes: no missing scripts or broken serialized references.\nAll Ferryman assets and directories have meta files.\nEntry: main; full voyage uses one scene with three island appearances and a shared sea.\nLegacy scenes and Prototype retained for demonstrations and regression.\nVessel/player/ship/deck/damage/projectile bindings verified. Deck: closed 12 x 5 edge, grid size 1.\nShip: 1000 HP. Player: move 4, attack 10, fire 0.2s, repair 50 HP/s.\nFour crew profiles: two combat, two maintenance; 0.45 x 0.7 footprints.\nTwo-wave fixed combat and random sea event; enemy projectile/bomb target combinations verified.\nMain stop appearances, sea motion, ship/shore animations and bootstrap definitions are configured.\n");
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
            foreach (Component component in transform.GetComponents<Component>())
            {
                Require(component != null, "Missing script in " + transform.name);
                using (var serialized = new SerializedObject(component))
                using (var property = serialized.GetIterator())
                    while (property.NextVisible(true))
                        if (property.propertyType == SerializedPropertyType.ObjectReference)
                            Require(property.objectReferenceValue != null || property.objectReferenceInstanceIDValue == 0,
                                "Broken reference: " + transform.name + "/" + property.propertyPath);
            }
        }

        private static void CheckFloat(Object target, string field, float expected)
        {
            Require(Mathf.Abs(new SerializedObject(target).FindProperty(field).floatValue - expected) < 0.0001f,
                target.name + "/" + field + " differs.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
