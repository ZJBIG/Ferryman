using Ferryman.Characters;
using UnityEngine;

namespace Ferryman.World
{
    public sealed class BoatStatusDisplay : MonoBehaviour
    {
        [SerializeField] private BoatHealth boat;
        [SerializeField] private PlayerCharacter player;

        private GUIStyle textStyle;

        private void OnGUI()
        {
            if (boat == null)
                return;
            if (textStyle == null)
                textStyle = new GUIStyle(GUI.skin.label) { fontSize = 18 };

            GUI.Box(new Rect(16f, 16f, 570f, 110f), GUIContent.none);
            GUI.Label(new Rect(30f, 24f, 540f, 28f),
                $"船只 HP  {boat.CurrentHealth:0} / {boat.MaxHealth:0}", textStyle);
            Color previous = GUI.color;
            GUI.color = new Color(0.15f, 0.2f, 0.25f);
            GUI.DrawTexture(new Rect(30f, 56f, 540f, 8f), Texture2D.whiteTexture);
            GUI.color = boat.IsDestroyed ? Color.red : new Color(0.3f, 0.85f, 0.55f);
            GUI.DrawTexture(new Rect(30f, 56f, 540f * boat.CurrentHealth / boat.MaxHealth, 8f), Texture2D.whiteTexture);
            GUI.color = previous;
            string state = boat.IsDestroyed ? "船只已毁，本轮失败"
                : player != null && player.IsStunned ? "眩晕中"
                : player != null && player.IsRepairing ? "维修中"
                : $"待维修点：{boat.RepairPoints.Count}";
            GUI.Label(new Rect(30f, 68f, 540f, 26f), state, textStyle);
            GUI.Label(new Rect(30f, 94f, 540f, 26f), "WASD 移动 · 左键射击 · 右键清障 · 按住 E 维修", textStyle);
        }
    }
}
