using System;
using System.IO;
using Unity.CodeEditor;
using UnityEditor;
using UnityEngine;

namespace Ferryman.Editor
{
    internal static class ScriptEditorSetup
    {
        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            if (!Application.isBatchMode)
                EditorApplication.delayCall += EnsureProject;
        }

        [MenuItem("Tools/Ferryman/Repair Script Editor")]
        private static void RepairProject()
        {
            EnsureProject();
            CodeEditor.CurrentEditor.SyncAll();
        }

        internal static void EnsureProject()
        {
            string editorPath = CodeEditor.CurrentEditorPath;
            bool repaired = false;

            // Unity stores the editor path globally; VS upgrades can leave a stale path.
            if (Path.IsPathRooted(editorPath) && !File.Exists(editorPath) && !Directory.Exists(editorPath))
            {
                string executable = Path.GetFileName(editorPath);
                foreach (var installation in CodeEditor.Editor.GetFoundScriptEditorPaths())
                {
                    string candidate = installation.Key;
                    if (!string.Equals(Path.GetFileName(candidate), executable, StringComparison.OrdinalIgnoreCase)
                        || (!File.Exists(candidate) && !Directory.Exists(candidate)))
                        continue;

                    CodeEditor.SetExternalScriptEditor(candidate);
                    Debug.Log($"Ferryman: restored script editor to {candidate}");
                    repaired = true;
                    break;
                }
            }

            string projectDirectory = Path.GetDirectoryName(Application.dataPath);
            if (repaired || (Directory.GetFiles(projectDirectory, "*.sln").Length == 0
                && Directory.GetFiles(projectDirectory, "*.slnx").Length == 0))
                CodeEditor.CurrentEditor.SyncAll();
        }
    }
}
