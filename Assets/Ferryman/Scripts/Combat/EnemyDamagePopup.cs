using System.Collections;
using System.Globalization;
using UnityEngine;

namespace Ferryman.Combat
{
    internal sealed class EnemyDamagePopup : MonoBehaviour
    {
        private static readonly Color Gold = new Color32(255, 228, 136, 255);
        private static readonly Color CriticalRed = new Color32(255, 84, 65, 255);
        private static readonly Color Ink = new Color32(77, 43, 31, 255);
        private static Font font;

        internal static void Play(float damage, Bounds monster, Vector3 impact, Material particleMaterial, bool critical)
        {
            var popup = new GameObject("Enemy Damage Popup").AddComponent<EnemyDamagePopup>();
            popup.transform.position = impact;
            popup.EmitSparks(particleMaterial);
            var label = new GameObject("Damage").transform;
            label.SetParent(popup.transform, false);
            float y = monster.max.y + 0.12f;
            Camera camera = Camera.main;
            if (camera != null && camera.orthographic)
                y = Mathf.Min(y, camera.transform.position.y + camera.orthographicSize - (critical ? 1.65f : 1.38f));
            label.position = new Vector3(Mathf.Lerp(monster.center.x, impact.x, 0.32f), y, monster.center.z);
            string value = damage.ToString(critical ? "0" : "0.#", CultureInfo.InvariantCulture);
            popup.StartCoroutine(popup.FloatLabel(label, value, critical));
        }

        private IEnumerator FloatLabel(Transform label, string value, bool critical)
        {
            Color face = critical ? CriticalRed : Gold;
            TextMesh shadow = CreateLabel(label, "Ink", value, Ink, 20);
            shadow.transform.localPosition = new Vector3(0.028f, -0.035f);
            TextMesh text = CreateLabel(label, "Value", value, face, 21);
            const float duration = 0.58f;
            float started = Time.time;
            Vector3 origin = label.position;
            while (Time.time - started < duration)
            {
                float progress = (Time.time - started) / duration;
                label.position = origin + Vector3.up * (0.95f * Mathf.Sqrt(progress));
                label.localScale = Vector3.one * ((critical ? 1.45f : 1f) * (1f + 0.22f * Mathf.Exp(-progress * 12f)));
                float alpha = 1f - Mathf.SmoothStep(0f, 1f, (progress - 0.42f) / 0.58f);
                text.color = new Color(face.r, face.g, face.b, alpha);
                shadow.color = new Color(Ink.r, Ink.g, Ink.b, alpha);
                yield return null;
            }
            Destroy(gameObject);
        }

        private static TextMesh CreateLabel(Transform parent, string name, string value, Color color, int order)
        {
            var label = new GameObject(name).AddComponent<TextMesh>();
            label.transform.SetParent(parent, false);
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.font = font;
            label.fontSize = 48;
            label.fontStyle = FontStyle.Bold;
            label.characterSize = 0.11f;
            label.anchor = TextAnchor.LowerCenter;
            label.text = value;
            label.color = color;
            var renderer = label.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = font.material;
            renderer.sortingOrder = order;
            return label;
        }

        private void EmitSparks(Material material)
        {
            var sparks = gameObject.AddComponent<ParticleSystem>();
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = sparks.main;
            main.loop = false;
            main.playOnAwake = false;
            main.gravityModifier = 0.3f;
            main.maxParticles = 6;
            var emission = sparks.emission;
            emission.enabled = false;
            var renderer = sparks.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = 19;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 1.5f;
            renderer.velocityScale = 0.1f;
            sparks.Play();
            for (int i = 0; i < main.maxParticles; i++)
            {
                Vector2 direction = Random.insideUnitCircle.normalized;
                var particle = new ParticleSystem.EmitParams
                {
                    startColor = Color.Lerp(Gold, new Color32(255, 166, 54, 255), Random.value),
                    startLifetime = Random.Range(0.12f, 0.24f),
                    startSize = Random.Range(0.045f, 0.09f),
                    velocity = direction * Random.Range(1.8f, 3.8f)
                };
                sparks.Emit(particle, 1);
            }
        }
    }
}
