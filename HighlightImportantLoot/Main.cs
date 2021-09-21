using HarmonyLib;
using Kingmaker;
using Kingmaker.PubSubSystem;
using Kingmaker.UI.Common;
using System;
using System.Reflection;
using UnityEngine;
using UnityModManagerNet;
using UniRx;
using UnityEngine.UI;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Components;
using Kingmaker.Items.Parts;
using Kingmaker.UI.MVVM._PCView.Slots;
using Kingmaker.UI.MVVM._VM.Slots;
using System.Linq;
using Owlcat.Runtime.UniRx;
using UnityEngine.EventSystems;

namespace HighlightImportantLoot
{
    public class LootController : MonoBehaviour
    {
        private Sprite m_highlight_sprite;
        private ItemSlotsGroupView[] m_items_view;
        private bool m_deferred_update = true;

        private void Awake()
        {
            Texture2D tex = new Texture2D(128, 128) { name = "CustomHighlightTexture" };

            for (int y = 0; y < tex.height; ++y)
            {
                for (int x = 0; x < tex.height; ++x)
                {
                    tex.SetPixel(x, y, Color.white);
                }
            }

            m_highlight_sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            m_highlight_sprite.name = "CustomHighlightSprite";
        }

        private void OnEnable()
        {
            m_items_view = null;
        }

        private void Update()
        {
            if (m_items_view == null)
            {
                m_items_view = GetComponentsInChildren<ItemSlotsGroupView>();

                foreach (ItemSlotsGroupView view in m_items_view)
                {
                    view.ViewModel.CollectionChangedCommand.Subscribe(delegate (bool _) { m_deferred_update = true; });
                }

                foreach (ItemSlotPCView pc_slot in GetComponentsInChildren<ItemSlotPCView>())
                {
                    pc_slot.OnDropAsObservable().Subscribe(delegate (PointerEventData _) { m_deferred_update = true; });
                }
            }

            if (m_deferred_update)
            {
                foreach (ItemSlotsGroupView view in m_items_view)
                {
                    foreach (IWidgetView widget in view.m_WidgetList.m_VisibleEntries)
                    {
                        ItemSlotView<ItemSlotVM> slot = widget as ItemSlotView<ItemSlotVM>;

                        if (slot == null) continue;

                        Transform highlight = slot.m_NotableLayer.transform.parent.Find("CustomHighlight");

                        if (highlight == null)
                        {
                            highlight = Instantiate(slot.m_NotableLayer.gameObject, slot.m_NotableLayer.transform.parent).transform;
                            highlight.name = "CustomHighlight";
                            highlight.SetSiblingIndex(1);
                            highlight.GetComponent<Image>().sprite = null;

                            DestroyImmediate(highlight.transform.Find("NotableLayerFX").gameObject);

                            GameObject new_highlight_border = Instantiate(highlight.gameObject, highlight);
                            new_highlight_border.name = "CustomHighlightBorder";
                            new_highlight_border.GetComponent<Image>().sprite = m_highlight_sprite;
                            new_highlight_border.gameObject.SetActive(true);

                            GameObject new_highlight_inner = Instantiate(highlight.gameObject, highlight);
                            new_highlight_inner.name = "CustomHighlightInner";
                            new_highlight_inner.GetComponent<Image>().sprite = m_highlight_sprite;
                            new_highlight_inner.GetComponent<RectTransform>().localScale = new Vector3(0.9f, 0.9f);
                            new_highlight_inner.gameObject.SetActive(true);
                        }

                        highlight.Find("CustomHighlightBorder").GetComponent<Image>().color = HighlightImportantLoot.Settings.HighlightBorderColour;
                        highlight.Find("CustomHighlightInner").GetComponent<Image>().color = HighlightImportantLoot.Settings.HighlightBackgroundColour;

                        bool enabled = slot.Item != null;

                        if (enabled)
                        {
                            CopyScroll scroll = slot.Item.Blueprint.GetComponent<CopyScroll>();
                            CopyRecipe recipe = slot.Item.Blueprint.GetComponent<CopyRecipe>();
                            ItemPartShowInfoCallback cb = slot.Item.Get<ItemPartShowInfoCallback>();

                            bool is_copyable_scroll = scroll != null && Game.Instance.Player.Party.Any(i => scroll.CanCopy(slot.Item, i));
                            bool is_unlearned_recipe = recipe != null && recipe.CanCopy(slot.Item, null);
                            bool is_unread_document = cb != null && (!cb.m_Settings.Once || !cb.m_Triggered);

                            enabled = (HighlightImportantLoot.Settings.HighlightLootables.HasFlag(HighlightLootableOptions.UnlearnedScrolls) && is_copyable_scroll) ||
                                (HighlightImportantLoot.Settings.HighlightLootables.HasFlag(HighlightLootableOptions.UnlearnedRecipes) && is_unlearned_recipe) ||
                                (HighlightImportantLoot.Settings.HighlightLootables.HasFlag(HighlightLootableOptions.UnreadDocuments) && is_unread_document);
                        }

                        highlight.gameObject.SetActive(enabled);
                    }
                }

                m_deferred_update = false;
            }
        }
    }

    public class AreaHandler : IAreaHandler
    {
        public void OnAreaDidLoad()
        {
            if (!HighlightImportantLoot.Enabled)
            {
                return;
            }

            string[] paths = new string[]
            {
                "LootPCView/Window/Collector", // lootable box
            };
            
            foreach (string path in paths)
            {
                Transform stash = Game.Instance.UI.MainCanvas.transform.Find(path);
                if (stash != null)
                {
                    stash.gameObject.AddComponent<LootController>();
                }
            }     
        }

        public void OnAreaBeginUnloading()
        { }
    }

    [Flags]
    public enum HighlightLootableOptions
    {
        None                = 0,
        UnlearnedScrolls    = 1 << 0,
        UnlearnedRecipes    = 1 << 1,
        UnreadDocuments     = 1 << 2
    }

    public class Settings : UnityModManager.ModSettings
    {
        public HighlightLootableOptions HighlightLootables =
            HighlightLootableOptions.UnlearnedScrolls |
            HighlightLootableOptions.UnlearnedRecipes |
            HighlightLootableOptions.UnreadDocuments;

        public Color HighlightBorderColour = new Color(255, 215, 0);
        public Color HighlightBackgroundColour = new Color(255, 215, 0);

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }

    public class HighlightImportantLoot
    {
        public static UnityModManager.ModEntry.ModLogger Logger;
        public static Settings Settings;
        public static bool Enabled;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Logger = modEntry.Logger;
            Settings = Settings.Load<Settings>(modEntry);

            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;

            Harmony harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            EventBus.Subscribe(new AreaHandler());

            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Enabled = value;
            return true;
        }

        private static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Highlight loot");
            GUILayout.EndHorizontal();

            HighlightLootableOptions new_options = HighlightLootableOptions.None;

            foreach (HighlightLootableOptions flag in Enum.GetValues(typeof(HighlightLootableOptions)))
            {
                if (flag == HighlightLootableOptions.None) continue;

                GUILayout.BeginHorizontal();
                if (GUILayout.Toggle(Settings.HighlightLootables.HasFlag(flag), $" {flag}"))
                {
                    new_options |= flag;
                }
                GUILayout.EndHorizontal();
            }

            Settings.HighlightLootables = new_options;

            GUILayout.Space(4);

            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical();
            GUILayout.Label("Highlight border colour ");
            GUILayout.Label("Highlight background colour ");
            GUILayout.EndVertical();

            GUILayout.BeginVertical();
            GUILayout.Label(" Red ");
            GUILayout.Label(" Red ");
            GUILayout.EndVertical();

            GUILayout.BeginVertical();
            string border_r_str = GUILayout.TextField(Settings.HighlightBorderColour.r.ToString());
            string bg_r_str = GUILayout.TextField(Settings.HighlightBackgroundColour.r.ToString());
            GUILayout.EndVertical();

            GUILayout.BeginVertical();
            GUILayout.Label(" Green ");
            GUILayout.Label(" Green ");
            GUILayout.EndVertical();

            GUILayout.BeginVertical();
            string border_g_str = GUILayout.TextField(Settings.HighlightBorderColour.g.ToString());
            string bg_g_str = GUILayout.TextField(Settings.HighlightBackgroundColour.g.ToString());
            GUILayout.EndVertical();

            GUILayout.BeginVertical();
            GUILayout.Label(" Blue ");
            GUILayout.Label(" Blue ");
            GUILayout.EndVertical();

            GUILayout.BeginVertical();
            string border_b_str = GUILayout.TextField(Settings.HighlightBorderColour.b.ToString());
            string bg_b_str = GUILayout.TextField(Settings.HighlightBackgroundColour.b.ToString());
            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            GUILayout.EndHorizontal();

            uint.TryParse(border_r_str, out uint border_r);
            uint.TryParse(border_g_str, out uint border_g);
            uint.TryParse(border_b_str, out uint border_b);

            uint.TryParse(bg_r_str, out uint bg_r);
            uint.TryParse(bg_g_str, out uint bg_g);
            uint.TryParse(bg_b_str, out uint bg_b);

            Settings.HighlightBorderColour = new Color(
                Math.Max(0, Math.Min(255, border_r)),
                Math.Max(0, Math.Min(255, border_g)),
                Math.Max(0, Math.Min(255, border_b)),
                255);

            Settings.HighlightBackgroundColour = new Color(
                Math.Max(0, Math.Min(255, bg_r)),
                Math.Max(0, Math.Min(255, bg_g)),
                Math.Max(0, Math.Min(255, bg_b)),
                255);

            GUILayout.Space(4);
        }

        private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            Settings.Save(modEntry);
        }
    }
}
