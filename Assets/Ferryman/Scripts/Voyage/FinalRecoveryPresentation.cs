using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace Ferryman.Voyage
{
    internal sealed class FinalRecoveryPresentation : MonoBehaviour
    {
        private const float DropSeconds = 0.58f;
        private const float FlightSeconds = 2.6f;
        private static readonly Vector2 BagOffset = new Vector2(-1.65f, -0.35f);
        private static readonly Vector2[] WreckageOffsets =
        {
            new Vector2(-0.7f, -1f),
            new Vector2(-0.95f, 0.78f),
            new Vector2(0.6f, -1.04f)
        };

        private readonly SpriteRenderer[] wreckage = new SpriteRenderer[WreckageOffsets.Length];
        private SpriteRenderer gull;
        private Sprite[] wingFrames;
        private SpriteRenderer bag;
        private Sprite closedBag;
        private LineRenderer towline;
        private Material ropeMaterial;
        private Vector2 baseBagPosition;
        private Vector2 stern;
        private bool bagFloating;
        private int collected;

        internal static FinalRecoveryPresentation Create(Transform vessel, Vector2 stern, Sprite gull, Sprite[] wingFrames)
        {
            Sprite open = Resources.Load<Sprite>("Voyage/FinalRecovery/BagOpen");
            Sprite closed = Resources.Load<Sprite>("Voyage/FinalRecovery/BagClosed");
            Sprite scrap = FindScrapSprite();
            if (open == null || closed == null || scrap == null || gull == null)
                throw new InvalidOperationException("终战回收需要回收袋、废料和海鸥素材。");

            var root = new GameObject("Final Recovery");
            root.transform.SetParent(vessel, false);
            var presentation = root.AddComponent<FinalRecoveryPresentation>();
            presentation.Initialize(vessel.InverseTransformPoint(stern), gull, wingFrames, open, closed, scrap);
            return presentation;
        }

        private static Sprite FindScrapSprite()
        {
            var encounter = Resources.Load<EncounterDefinition>("Voyage/Events/GarbageTideColossus");
            return encounter?.waves?.FirstOrDefault()?.enemies?.FirstOrDefault()?.prefab?.BombPrefab
                ?.GetComponentsInChildren<SpriteRenderer>(true)
                .FirstOrDefault(renderer => renderer.sprite != null && renderer.sprite.name == "TrashBomb")?.sprite;
        }

        private void Initialize(Vector2 attachment, Sprite carrier, Sprite[] frames, Sprite open, Sprite closed, Sprite scrap)
        {
            stern = attachment;
            baseBagPosition = stern + BagOffset;
            wingFrames = frames ?? Array.Empty<Sprite>();
            closedBag = closed;
            gull = AddSprite("Recovery Gull", carrier, 16);
            gull.transform.localScale = Vector3.one * 1.3f;
            bag = AddSprite("Recovery Bag", open, 3);

            for (int i = 0; i < wreckage.Length; i++)
            {
                var piece = AddSprite("Floating Wreckage " + (i + 1), scrap, 2);
                piece.transform.localPosition = baseBagPosition + WreckageOffsets[i];
                piece.transform.localScale = Vector3.one * (0.62f / scrap.bounds.size.y);
                piece.transform.localRotation = Quaternion.Euler(0f, 0f, -27f + 43f * i);
                piece.color = new Color(0.79f + 0.07f * i, 0.9f, 0.93f);
                wreckage[i] = piece;
            }
        }

        internal IEnumerator DropBag()
        {
            foreach (var piece in wreckage)
                piece.enabled = true;

            gull.enabled = true;
            bag.enabled = true;
            bag.sortingOrder = 15;

            Camera camera = Camera.main;
            float halfWidth = camera == null ? 12f : camera.orthographicSize * camera.aspect;
            float viewCenter = camera == null ? 0f : transform.InverseTransformPoint(camera.transform.position).x;
            float start = Mathf.Min(baseBagPosition.x - 5f, viewCenter - halfWidth - 2f);
            float finish = Mathf.Max(baseBagPosition.x + 8f, viewCenter + halfWidth + 2f);
            Vector2 releasePosition = baseBagPosition + new Vector2(0f, 1.95f);
            float release = FlightSeconds * Mathf.InverseLerp(start, finish, releasePosition.x);
            float duration = Mathf.Max(FlightSeconds, release + DropSeconds);
            Vector2 carried = Vector2.zero;
            for (float elapsed = 0f; ; elapsed = Mathf.Min(elapsed + Time.deltaTime, duration))
            {
                if (!this)
                    yield break;
                float passedRelease = elapsed - release;
                var flight = new Vector2(Mathf.Lerp(start, finish, elapsed / FlightSeconds),
                    releasePosition.y + Mathf.Sin(Mathf.Min(Mathf.Abs(passedRelease), 1.4f)) * 0.66f);
                gull.transform.localPosition = flight;
                if (wingFrames.Length > 0)
                    gull.sprite = wingFrames[Mathf.FloorToInt(elapsed * 8f) % wingFrames.Length];

                if (passedRelease < 0f)
                {
                    carried = flight + Vector2.down * gull.sprite.bounds.extents.y * 1.2f;
                    bag.transform.localPosition = carried;
                    bag.transform.localScale = Vector3.one * 0.66f;
                }
                else if (!bagFloating)
                {
                    float falling = Mathf.Clamp01(passedRelease / DropSeconds);
                    bag.transform.localPosition = Vector2.Lerp(carried, baseBagPosition, falling * falling);
                    bag.transform.localScale = Vector3.one * Mathf.Lerp(0.66f, 1f, falling);
                    if (elapsed >= release + DropSeconds)
                    {
                        bag.sortingOrder = 3;
                        bagFloating = true;
                    }
                }
                if (elapsed >= duration)
                    break;
                yield return null;
            }

            gull.enabled = false;
        }

        internal IEnumerator CollectNext()
        {
            if (collected >= wreckage.Length)
                yield break;

            SpriteRenderer piece = wreckage[collected++];
            Vector3 start = piece.transform.localPosition;
            Vector3 size = piece.transform.localScale;
            const float seconds = 0.33f;
            for (float elapsed = 0f; elapsed < seconds; elapsed += Time.deltaTime)
            {
                if (!this)
                    yield break;
                float amount = Mathf.SmoothStep(0f, 1f, elapsed / seconds);
                Vector3 inside = bag.transform.localPosition + Vector3.up * 0.26f;
                piece.transform.localPosition = Vector3.Lerp(start, inside, amount)
                    + Vector3.up * (Mathf.Sin(amount * Mathf.PI) * 0.27f);
                piece.transform.localScale = size * Mathf.Lerp(1f, 0.4f, amount);
                yield return null;
            }
            piece.enabled = false;
            bag.transform.localScale = Vector3.one * (1f + 0.12f * collected);
        }

        internal void BeginTow()
        {
            bag.sprite = closedBag;
            bag.transform.localScale = Vector3.one;
            baseBagPosition += new Vector2(-0.28f, 0.04f);

            var rope = new GameObject("Recovery Towline");
            rope.transform.SetParent(transform, false);
            towline = rope.AddComponent<LineRenderer>();
            ropeMaterial = new Material(Shader.Find("Sprites/Default"));
            towline.sharedMaterial = ropeMaterial;
            towline.sortingOrder = 2;
            towline.useWorldSpace = false;
            towline.positionCount = 3;
            towline.startWidth = towline.endWidth = 0.09f;
            towline.numCapVertices = 3;
            towline.startColor = towline.endColor = new Color(0.78f, 0.66f, 0.47f);
            Update();
        }

        internal void FinishTow()
        {
            gameObject.SetActive(false);
            Destroy(gameObject);
        }

        private void Update()
        {
            for (int i = collected; i < wreckage.Length; i++)
            {
                SpriteRenderer piece = wreckage[i];
                if (piece != null && piece.enabled)
                {
                    float drift = Mathf.Sin(Time.time * 3.5f + i * 1.6f) * 0.09f;
                    piece.transform.localPosition = stern + BagOffset + WreckageOffsets[i] + Vector2.up * drift;
                }
            }
            if (!bagFloating)
                return;
            float bob = Mathf.Sin(Time.time * 5f) * 0.08f;
            bag.transform.localPosition = baseBagPosition + Vector2.up * bob;
            if (towline == null)
                return;
            Vector2 bagTie = (Vector2)bag.transform.localPosition + new Vector2(0.24f, 0.33f);
            towline.SetPosition(0, stern);
            towline.SetPosition(1, Vector2.Lerp(stern, bagTie, 0.5f) + Vector2.down * 0.11f);
            towline.SetPosition(2, bagTie);
        }

        private SpriteRenderer AddSprite(string objectName, Sprite sprite, int sortingOrder)
        {
            var visual = new GameObject(objectName);
            visual.transform.SetParent(transform, false);
            var renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = sortingOrder;
            renderer.enabled = false;
            return renderer;
        }

        private void OnDestroy()
        {
            if (ropeMaterial != null)
                Destroy(ropeMaterial);
        }
    }
}
