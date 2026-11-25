using System;
using System.IO;
using UnityEngine;

namespace Ferryman.Voyage
{
    public sealed class VoyageBootstrap : MonoBehaviour
    {
        [SerializeField] private VoyageDefinition definition;
        [SerializeField] private VoyageUIArt uiArt;

        private void Awake()
        {
            if (FindObjectOfType<VoyageController>() != null)
                return;

            var root = new GameObject("Voyage");
            var controller = root.AddComponent<VoyageController>();
            root.AddComponent<VoyageView>().Initialize(uiArt);
            try
            {
                controller.Initialize(definition,
                    new VoyageSaveStore(Path.Combine(Application.persistentDataPath, "Voyage")));
                root.AddComponent<VoyageAudio>();
            }
            catch (Exception error)
            {
                DontDestroyOnLoad(root);
                controller.ReportError(error);
            }
        }
    }
}
