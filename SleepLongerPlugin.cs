// TODO: Gamepad/keyboard support
// TODO: 72h/168h marks on slider
// Todo: Nicer 3day/7day skyline
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using UnityEngine;
using UnityEngine.UI;

namespace SleepLonger
{
    [BepInPlugin(GUID, NAME, VERSION)]
    public class SleepLongerPlugin : BaseUnityPlugin
    {
        public const string NAME = "SleepLonger";
        public const string VERSION = "1.1.0";
        public const string CREATOR = "Jdbye";
        public const string GUID = CREATOR + NAME;

        internal static ManualLogSource Log;

        internal void Awake()
        {
            Log = this.Logger;
            Log.LogInfo($"{NAME} {VERSION} by {CREATOR} loaded!");

            new Harmony(GUID).PatchAll();
        }

        internal void Update()
        {

        }

        internal static GameObject GetCheckBoxTemplate(CharacterUI characterUI)
        {
            // We use the "Show HUD" checkbox from options.
            // No particular reason, it's just a convenient one to use.
            var orig = characterUI.transform.Find("Canvas/GeneralPanels/Options - Panel(Clone)/Window/Panel/Content/Scroll View/Viewport/Content/General/Gameplay/chkShowHud");
            return orig.gameObject;
        }

        internal static UIToggleButton CreateCheckBox(GameObject checkBoxTemplate, Transform parent, string name, string label)
        {
            GameObject newChk = GameObject.Instantiate(checkBoxTemplate, parent); // Create a clone of the template checkbox
            newChk.transform.localPosition = Vector3.zero; // Reset position of checkbox
            newChk.transform.localRotation = Quaternion.identity; // Reset rotation of checkbox
            UIToggleButton button = newChk.GetComponent<UIToggleButton>(); // Find the UIToggleButton component
            newChk.name = name; // Change name of toggle button. Not strictly needed
            var labelComponent = newChk.GetComponentInChildren<UnityEngine.UI.Text>(true); // Find the Text component of the label
            GameObject.Destroy(labelComponent.GetComponent<UILocalize>()); // Destroy the UILocalize component since it will override our label
            labelComponent.name = "lblCheckBoxHeader"; // Change name of label. Not strictly needed
            button.Text = label; // Set our label
            button.onValueChanged.RemoveAllListeners(); // Remove existing listeners
            return button;
        }

        internal static void InitSkylineValues(RestingMenu restingMenu, int hours)
        {
            // Copied from Outward, modified to allow higher max value for the cursors
            hoursAll[restingMenu.CharacterUI.gameObject.name] = hours;
            float hoursf = (float)hours;

            if (restingMenu.m_skylineScrollRect)
            {
                restingMenu.m_skylineStartingToDRatio = (float)restingMenu.m_skylineStartingTime / 24F;
                float widthOfSingleImage = restingMenu.m_skylineScrollRect.content.rect.width / (float)restingMenu.m_skylineScrollRect.content.childCount;
                float widthRatio = restingMenu.m_skylineScrollRect.viewport.rect.width / widthOfSingleImage;
                float maxValue = hoursf * widthRatio;
                if (restingMenu.m_sldLocalPlayerCursor)
                {
                    restingMenu.m_sldLocalPlayerCursor.maxValue = maxValue;
                }
                if (restingMenu.m_sldOtherPlayerCursors != null)
                {
                    for (int i = 0; i < restingMenu.m_sldOtherPlayerCursors.Length; i++)
                    {
                        if (restingMenu.m_sldOtherPlayerCursors[i])
                        {
                            restingMenu.m_sldOtherPlayerCursors[i].maxValue = maxValue;
                        }
                    }
                }
            }

            restingMenu.m_skylineInitialized = true;
        }

        internal static void RefreshSkylinePosition(RestingMenu restingMenu)
        {
            // Runs alongside the stock Outward RefreshSkylinePosition to update the position of the extra skylines
            var dayPanorama = restingMenu.transform.Find("Content/DayPanorama");

            float adjustedRatio = EnvironmentConditions.Instance.TODRatio - restingMenu.m_skylineStartingToDRatio;
            if (adjustedRatio < 0f) adjustedRatio += 1f;

            var scrollView3Day = dayPanorama.Find("ScrollView3Day");
            if (scrollView3Day)
            {
                scrollView3Day.GetComponent<ScrollRect>().horizontalNormalizedPosition = adjustedRatio;
            }

            var scrollView7Day = dayPanorama.Find("ScrollView7Day");
            if (scrollView7Day)
            {
                scrollView7Day.GetComponent<ScrollRect>().horizontalNormalizedPosition = adjustedRatio;
            }
        }

        internal static void SetMaxTime(RestingMenu restingMenu, int hours, bool forceSetRestLength = false)
        {
            // Reset the time values to 0 or set them to max automatically depending on whether sleep 3 days or 7 days was selected
            // Also adjust the skyline
            // if forceSetRestLength is true, the time values are set to max automatically for 24 hour sleep too

            InitSkylineValues(restingMenu, hours);

            if (hours > 24 || forceSetRestLength)
            {
                int guardHours = 0;

                if (CharacterManager.Instance.BaseAmbushProbability > 0)
                {
                    // Calculate adjusted ambush probability based on any ambush reduction of tents
                    float ambushProbability = CharacterManager.Instance.BaseAmbushProbability;

                    int ambushReduction = int.MaxValue;
                    for (int j = 0; j < CharacterManager.Instance.PlayerCharacters.Count; j++)
                    {
                        Character character2 = CharacterManager.Instance.Characters[CharacterManager.Instance.PlayerCharacters.Values[j]];
                        if (character2 != null)
                        {
                            if (character2.CharacterResting.RestContainer)
                            {
                                ambushReduction = Mathf.Min(ambushReduction, character2.CharacterResting.RestContainer.AmbushReduction);
                            }
                        }
                    }
                    if (ambushReduction == int.MaxValue) ambushReduction = 0;
                    else ambushProbability -= ambushReduction;

                    Log.LogDebug($"Ambush probability: {CharacterManager.Instance.BaseAmbushProbability} Reduction: {ambushReduction} Adjusted: {ambushProbability}");

                    // If ambush reduction reduces our probability to <= 0, we don't need to guard
                    // Otherwise, we need to guard half the total resting time to reduce the probability to 0, no matter what the probability is
                    if (ambushProbability > 0) guardHours = hours / 2;
                    else guardHours = 0;
                }

                // Some reasonable defaults for long sleeps, to minimize mana loss
                int sleepHours = hours / 6;
                int repairHours = hours - (guardHours + sleepHours);

                // Update the slider values to max them out
                foreach (var display in restingMenu.m_restingActivityDisplays)
                {
                    switch (display.Activity.Type)
                    {
                        case RestingActivity.ActivityTypes.Sleep:
                            display.m_timeSelector.ChangeValue(sleepHours);
                            display.m_timeSelector.SetValue(sleepHours);
                            break;
                        case RestingActivity.ActivityTypes.Guard:
                            display.m_timeSelector.ChangeValue(guardHours);
                            display.m_timeSelector.SetValue(guardHours);
                            break;
                        case RestingActivity.ActivityTypes.Repair:
                            display.m_timeSelector.ChangeValue(repairHours);
                            display.m_timeSelector.SetValue(repairHours);
                            break;
                        default:
                            display.m_timeSelector.ChangeValue(0);
                            display.m_timeSelector.SetValue(0);
                            break;
                    }
                }
            }
            else
            {
                // We are not sleeping for 3 or 7 days, reset the sliders to 0
                foreach (var display in restingMenu.m_restingActivityDisplays)
                {
                    display.m_timeSelector.ChangeValue(0);
                    display.m_timeSelector.SetValue(0);
                }
            }
        }

        internal static void UpdatePanel(RestingMenu restingMenu, int hours)
        {
            // Overrides the stock Outward UpdatePanel only if needed (3 or 7 day sleep is selected)
            // This is needed 
            restingMenu.RefreshSkylinePosition();
            RefreshSkylinePosition(restingMenu);
            int totalRestHours = 0;
            bool allPlayersInRestContainer = true;
            bool donePreparingRest = true;
            if (Global.Lobby.PlayersInLobby.Count - 1 != restingMenu.m_otherPlayerUIDs.Count)
            {
                restingMenu.InitPlayerCursors();
                allPlayersInRestContainer = false;
                donePreparingRest = false;
            }
            else
            {
                for (int i = 0; i < restingMenu.m_otherPlayerUIDs.Count; i++)
                {
                    Character characterFromPlayer = CharacterManager.Instance.GetCharacterFromPlayer(restingMenu.m_otherPlayerUIDs[i]);
                    if (characterFromPlayer != null)
                    {
                        if (CharacterManager.Instance.RestingPlayerUIDs.Contains(characterFromPlayer.UID))
                        {
                            donePreparingRest &= characterFromPlayer.CharacterResting.DonePreparingRest;
                        }
                        else
                        {
                            allPlayersInRestContainer = false;
                        }
                        restingMenu.m_sldOtherPlayerCursors[i].value = characterFromPlayer.CharacterResting.TotalRestTime;
                    }
                    else
                    {
                        allPlayersInRestContainer = false;
                    }
                }
            }
            for (int j = 0; j < SplitScreenManager.Instance.LocalPlayerCount; j++)
            {
                allPlayersInRestContainer &= (SplitScreenManager.Instance.LocalPlayers[j].AssignedCharacter != null);
            }
            donePreparingRest = (donePreparingRest && allPlayersInRestContainer);
            restingMenu.m_restingCanvasGroup.interactable = (allPlayersInRestContainer && !restingMenu.LocalCharacter.CharacterResting.DonePreparingRest);
            if (restingMenu.m_waitingForOthers)
            {
                if (restingMenu.m_waitingForOthers.gameObject.activeSelf == restingMenu.m_restingCanvasGroup.interactable)
                {
                    restingMenu.m_waitingForOthers.gameObject.SetActive(!restingMenu.m_restingCanvasGroup.interactable);
                }
                if (restingMenu.m_waitingText && restingMenu.m_waitingForOthers.gameObject.activeSelf)
                {
                    restingMenu.m_waitingText.text = LocalizationManager.Instance.GetLoc(donePreparingRest ? "Rest_Title_Resting" : "Sleep_Title_Waiting");
                }
            }
            for (int k = 0; k < restingMenu.m_restingActivityDisplays.Length; k++)
            {
                totalRestHours += restingMenu.m_restingActivityDisplays[k].AssignedTime;
            }
            for (int l = 0; l < restingMenu.m_restingActivityDisplays.Length; l++)
            {
                if (restingMenu.ActiveActivities[l] != RestingActivity.ActivityTypes.Guard || CharacterManager.Instance.BaseAmbushProbability > 0)
                {
                    restingMenu.m_restingActivityDisplays[l].MaxValue = hours - (totalRestHours - restingMenu.m_restingActivityDisplays[l].AssignedTime);
                }
                else
                {
                    restingMenu.m_restingActivityDisplays[l].MaxValue = 0;
                }
            }
            if (restingMenu.m_sldLocalPlayerCursor)
            {
                restingMenu.m_sldLocalPlayerCursor.value = (float)totalRestHours;
            }
            bool totalRestHoursChanged = false;
            if (restingMenu.m_lastTotalRestTime != totalRestHours)
            {
                totalRestHoursChanged = true;
                restingMenu.m_lastTotalRestTime = totalRestHours;
            }
            restingMenu.RefreshOverviews(totalRestHoursChanged && !restingMenu.m_tryRest);
        }

        private static Texture2D GetReadableTexture2D(Texture texture)
        {
            // Makes a Texture2D compatible with GetPixels by blitting it to a RenderTexture and reading the pixels back
            // Not currently used
            var tmp = RenderTexture.GetTemporary(
                texture.width,
                texture.height,
                0,
                RenderTextureFormat.Default,
                RenderTextureReadWrite.Linear
            );
            Graphics.Blit(texture, tmp);

            var previousRenderTexture = RenderTexture.active;
            RenderTexture.active = tmp;

            var texture2d = new Texture2D(texture.width, texture.height);
            texture2d.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            texture2d.Apply();

            RenderTexture.active = previousRenderTexture;
            RenderTexture.ReleaseTemporary(tmp);
            return texture2d;
        }

        internal static Texture2D Crop(Texture2D sourceTexture, Rect cropRect)
        {
            // Crops a Texture2D, not currently used
            var cropRectInt = new RectInt
            (
                Mathf.FloorToInt(cropRect.x),
                Mathf.FloorToInt(cropRect.y),
                Mathf.FloorToInt(cropRect.width),
                Mathf.FloorToInt(cropRect.height)
            );

            Texture2D tex = GetReadableTexture2D(sourceTexture);

            var newPixels = tex.GetPixels(cropRectInt.x, cropRectInt.y, cropRectInt.width, cropRectInt.height);
            var newTexture = new Texture2D(cropRectInt.width, cropRectInt.height);
            newTexture.SetPixels(newPixels);
            newTexture.Apply();
            return newTexture;
        }

        internal static System.Collections.Generic.Dictionary<string, int> hoursAll = new();

        [HarmonyPatch(typeof(RestingMenu))]
        public class RestingMenuPatches
        {
            [HarmonyPatch(nameof(RestingMenu.StartInit)), HarmonyPostfix]
            static void RestingMenu_StartInit_Postfix(RestingMenu __instance)
            {
                // This patch injects the checkboxes for 3 day and 7 day sleep. Very janky with lots of hardcoded values
                try
                {
                    hoursAll.Remove(__instance.CharacterUI.gameObject.name);

                    const float spacing = 16;
                    const float height = 30;
                    const float total = spacing + height;
                    const float moveTitle = total / 9.2F;
                    var transform = __instance.transform as RectTransform;
                    var content = transform.Find("Content");
                    var title = content.Find("lblTitle");
                    var dayPanorama = content.Find("DayPanorama");
                    var restingActivitiesPanel = content.Find("RestingActivitiesPanel");
                    var description = restingActivitiesPanel.Find("lblDescription");
                    var restingActivities = restingActivitiesPanel.Find("RestingActivities") as RectTransform;
                    var restingMenuSeparator = content.Find("Separator");
                    var chkTemplate = GetCheckBoxTemplate(__instance.CharacterUI);

                    // Resize the menu panel
                    transform.sizeDelta = new(transform.sizeDelta.x, transform.sizeDelta.y + total);
                    // Move existing UI elements to accomodate the new checkboxes
                    restingActivities.localPosition = new(restingActivities.localPosition.x, restingActivities.localPosition.y + (total / 2), restingActivities.localPosition.z);
                    restingMenuSeparator.localPosition = new(restingMenuSeparator.localPosition.x, restingMenuSeparator.localPosition.y - (total / 2), restingMenuSeparator.localPosition.z);
                    title.localPosition = new(title.localPosition.x, title.localPosition.y - moveTitle, title.localPosition.z);
                    description.localPosition = new(description.localPosition.x, description.localPosition.y + (total / 2), description.localPosition.z);
                    foreach (Transform child in restingActivitiesPanel)
                    {
                        // Move the separators related to RestingActivities
                        if (child.name == "Separator")
                            child.localPosition = new(child.localPosition.x, child.localPosition.y + (total / 2), child.localPosition.z);
                    }


                    // Add our 3-day and 7-day skylines

                    var scrollView = dayPanorama.Find("Scroll View");
                    var baseSiblingIndex = scrollView.GetSiblingIndex();
                    var skyline3Day = GameObject.Instantiate(scrollView, dayPanorama);
                    skyline3Day.name = "ScrollView3Day";
                    skyline3Day.SetSiblingIndex(baseSiblingIndex + 1);
                    var skyline7Day = GameObject.Instantiate(scrollView, dayPanorama);
                    skyline7Day.name = "ScrollView7Day";
                    skyline7Day.SetSiblingIndex(baseSiblingIndex + 2);

                    var skyline3DayContent = skyline3Day.Find("Viewport/Content");
                    var skyline3DayImage = skyline3DayContent.Find("Tile1");
                    for (int i = 3; i < 5; i++)
                    {
                        // Add images
                        var addedImage = GameObject.Instantiate(skyline3DayImage, skyline3DayContent);
                        addedImage.name = $"Tile{i}";
                        addedImage.SetAsLastSibling();
                    }
                    foreach (RectTransform t in skyline3DayContent)
                    {
                        // Scale images by 1/3 horizontally
                        var layout = t.gameObject.GetComponent<LayoutElement>();
                        layout.minWidth = layout.minWidth / 3;
                        t.gameObject.GetComponent<UnityEngine.UI.Image>().preserveAspect = false;
                        t.sizeDelta = new Vector3(t.sizeDelta.x * (1F / 3F), t.sizeDelta.y);
                    }

                    var skyline7DayContent = skyline7Day.Find("Viewport/Content");
                    var skyline7DayImage = skyline7DayContent.Find("Tile1");
                    var imageComponent = skyline7DayImage.gameObject.GetComponent<UnityEngine.UI.Image>();
                    for (int i = 3; i < 9; i++)
                    {
                        // Add images
                        var addedImage = GameObject.Instantiate(skyline7DayImage, skyline7DayContent);
                        addedImage.name = $"Tile{i}";
                        addedImage.SetAsLastSibling();
                    }
                    foreach (RectTransform t in skyline7DayContent)
                    {
                        // Scale images by 1/7 horizontally
                        var layout = t.gameObject.GetComponent<LayoutElement>();
                        layout.minWidth = layout.minWidth / 7;
                        t.gameObject.GetComponent<UnityEngine.UI.Image>().preserveAspect = false;
                        t.sizeDelta = new Vector3(t.sizeDelta.x * (1F / 7F), t.sizeDelta.y);
                    }

                    skyline3Day.gameObject.SetActive(false);
                    skyline7Day.gameObject.SetActive(false);

                    // MenuManager/CharacterUIs/PlayerChar HNj80z8ieE6-5Ouwh_n0vg_HNj80z8ieE6-5Ouwh_n0vg UI/Canvas/GameplayPanels/Menus/ModalMenus/RestingMenu/Content/DayPanorama/Scroll View/Viewport/Content/

                    // Create the checkboxes
                    var chk1Day = CreateCheckBox(chkTemplate, restingActivitiesPanel, "chkSleep1Day", "Sleep 1 Day");
                    var chk3Days = CreateCheckBox(chkTemplate, restingActivitiesPanel, "chkSleep3Days", "Sleep 3 Days");
                    var chk7Days = CreateCheckBox(chkTemplate, restingActivitiesPanel, "chkSleep7Days", "Sleep 7 Days");

                    // Destroy the separators from the cloned checkboxes
                    GameObject.Destroy(chk1Day.transform.Find("Separator").gameObject);
                    GameObject.Destroy(chk3Days.transform.Find("Separator").gameObject);
                    GameObject.Destroy(chk7Days.transform.Find("Separator").gameObject);

                    // Set sibling order
                    chk1Day.transform.SetAsLastSibling();
                    chk3Days.transform.SetAsLastSibling();
                    chk7Days.transform.SetAsLastSibling();

                    // Move the checkboxes to the appropriate location
                    var t1 = chk1Day.transform as RectTransform;
                    var t3 = chk3Days.transform as RectTransform;
                    var t7 = chk7Days.transform as RectTransform;
                    t1.sizeDelta = new(150, 30);
                    t3.sizeDelta = new(150, 30);
                    t7.sizeDelta = new(150, 30);
                    t1.localPosition = new(-160, -66, t1.localPosition.z);
                    t3.localPosition = new(0, -66, t3.localPosition.z);
                    t7.localPosition = new(170, -66, t7.localPosition.z);

                    // Add value changed listeners to the checkboxes

                    chk1Day.onValueChanged.AddListener(delegate
                    {
                        if (chk1Day.isOn)
                        {
                            chk3Days.isOn = false;
                            chk7Days.isOn = false;

                            SetMaxTime(__instance, 24, true);
                        }
                        else
                        {
                            if (chk7Days.isOn) SetMaxTime(__instance, 24 * 7);
                            else if (chk3Days.isOn) SetMaxTime(__instance, 24 * 3);
                            else SetMaxTime(__instance, 24, false);
                        }
                    });

                    chk3Days.onValueChanged.AddListener(delegate
                    {
                        skyline3Day.gameObject.SetActive(chk3Days.isOn);

                        if (chk3Days.isOn)
                        {
                            chk1Day.isOn = false;
                            chk7Days.isOn = false;

                            SetMaxTime(__instance, 24 * 3);
                        }
                        else
                        {
                            if (chk7Days.isOn) SetMaxTime(__instance, 24 * 7);
                            else SetMaxTime(__instance, 24, chk1Day.isOn);
                        }
                    });

                    chk7Days.onValueChanged.AddListener(delegate
                    {
                        skyline7Day.gameObject.SetActive(chk7Days.isOn);

                        if (chk7Days.isOn)
                        {
                            chk1Day.isOn = false;
                            chk3Days.isOn = false;

                            SetMaxTime(__instance, 24 * 7);
                        }
                        else
                        {
                            if (chk3Days.isOn) SetMaxTime(__instance, 24 * 3);
                            else SetMaxTime(__instance, 24, chk1Day.isOn);
                        }
                    });

                    Log.LogInfo($"Patched RestingMenu UI for player {__instance.CharacterUI.TargetCharacter.Name}");
                }
                catch (Exception e)
                {
                    Log.LogError($"Failed to patch RestingMenu UI for player {__instance.CharacterUI.TargetCharacter.Name} ({e.GetType()}): {e.Message}");
                    Log.LogDebug($"Stack trace: {e.StackTrace}");
                }
            }

            [HarmonyPatch(nameof(RestingMenu.ResetMenu)), HarmonyPostfix]
            static void RestingMenu_ResetMenu_Postfix(RestingMenu __instance)
            {
                // Reset the checkboxes and skylines

                try
                {
                    var restingActivitiesPanel = __instance.transform.Find("Content/RestingActivitiesPanel");
                    var chkSleep1Day = restingActivitiesPanel.Find("chkSleep1Day")?.GetComponent<UIToggleButton>();
                    var chkSleep3Days = restingActivitiesPanel.Find("chkSleep3Days")?.GetComponent<UIToggleButton>();
                    var chkSleep7Days = restingActivitiesPanel.Find("chkSleep7Days")?.GetComponent<UIToggleButton>();
                    if (chkSleep1Day != null) chkSleep1Day.isOn = false;
                    if (chkSleep3Days != null) chkSleep3Days.isOn = false;
                    if (chkSleep7Days != null) chkSleep7Days.isOn = false;


                    var dayPanorama = __instance.transform.Find("Content/DayPanorama");

                    var scrollView3Day = dayPanorama.Find("ScrollView3Day");
                    var scrollView7Day = dayPanorama.Find("ScrollView7Day");

                    if (scrollView3Day) scrollView3Day.gameObject.SetActive(false);
                    if (scrollView7Day) scrollView7Day.gameObject.SetActive(false);
                }
                catch (Exception e)
                {
                    Log.LogError($"ResetMenu Postfix failed for player {__instance.CharacterUI.TargetCharacter.Name} ({e.GetType()}): {e.Message}");
                    Log.LogDebug($"Stack trace: {e.StackTrace}");
                }
            }

            [HarmonyPatch(nameof(RestingMenu.UpdatePanel)), HarmonyPrefix]
            static bool RestingMenu_UpdatePanel_Prefix(RestingMenu __instance)
            {
                // This patch overrides UpdatePanel when needed to increase the maximum hour count, but defaults to the stock UpdatePanel when 3/7 day sleep is not selected

                try
                {
                    if (hoursAll.ContainsKey(__instance.CharacterUI.gameObject.name))
                    {
                        UpdatePanel(__instance, hoursAll[__instance.CharacterUI.gameObject.name]);
                        return false;
                    }
                }
                catch (Exception e)
                {
                    Log.LogError($"UpdatePanel Prefix failed for player {__instance.CharacterUI.TargetCharacter.Name} ({e.GetType()}): {e.Message}");
                    Log.LogDebug($"Stack trace: {e.StackTrace}");
                }

                return true;
            }
        }
    }
}