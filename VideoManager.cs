// =============================================================================
// VideoManager.cs
// Purpose: Manages 360° video playback on Quest Pro with a VR selection menu
// Attach to: DataCollectionManager (same GameObject as DataCollector)
// Project: STAR-VP Quest Pro Data Collection
// Unity: 2022.3 LTS + Meta XR All-in-One SDK
//
// HOW IT WORKS:
//   1. On launch, shows a VR menu listing all configured videos
//   2. User navigates with thumbstick (up/down) and selects with A or Trigger
//   3. Selected video plays on the 360° sphere
//   4. When video ends, the menu reappears so user can pick the next video
//   5. Completed videos are marked with ✓ in the menu
//   6. DataCollector is notified on every video start/stop
//
// VIDEO FILE LOCATION ON QUEST PRO:
//   Videos are stored in the app's own data directory to avoid permission issues.
//   Copy videos with:
//     adb push <local_file> /sdcard/Android/data/com.research.vrdatacollector/files/Videos/
//   URLs set in Inspector (e.g. file:///sdcard/Movies/VRStudy/video_01.mp4) are
//   automatically remapped to the app data path at runtime.
//
// VR MENU CONTROLS:
//   Left/Right Thumbstick Up/Down → navigate list
//   A button  /  Either Index Trigger → play selected video
//   (Arrow keys + Enter in Unity Editor for testing)
// =============================================================================

using UnityEngine;
using UnityEngine.Video;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

public class VideoManager : MonoBehaviour
{
    // =========================================================================
    // INSPECTOR REFERENCES
    // =========================================================================
    [Header("References")]
    [Tooltip("Drag the VideoSphere (which has VideoPlayer) here")]
    public VideoPlayer videoPlayer;

    [Tooltip("Drag the DataCollectionManager (which has DataCollector) here")]
    public DataCollector dataCollector;

    // =========================================================================
    // VIDEO CONFIGURATION
    // =========================================================================
    [Header("Video List")]
    [Tooltip("Full URLs to video files on Quest Pro storage")]
    public List<string> videoURLs = new List<string>();

    [Tooltip("Video IDs matching videoURLs (same order, same count)")]
    public List<string> videoIDs = new List<string>();

    // =========================================================================
    // MENU SETTINGS
    // =========================================================================
    [Header("Video Selection Menu")]
    [Tooltip("Distance of menu from camera (meters)")]
    public float menuDistance = 2.5f;

    [Tooltip("World-space scale of the menu canvas (0.002 = ~1.6m wide)")]
    public float menuScale = 0.002f;

    // =========================================================================
    // PRIVATE STATE
    // =========================================================================
    private int currentVideoIndex = -1;
    private bool isTransitioning = false;
    private bool videoPreparing = false;
    private bool videoEndDetected = false;

    // Menu state
    private GameObject menuRoot;
    private List<Image> itemBackgrounds = new List<Image>();
    private List<Text> itemTexts = new List<Text>();
    private int selectedMenuIndex = 0;
    private bool menuVisible = false;
    private bool thumbstickCooldown = false;
    private HashSet<int> completedVideos = new HashSet<int>();
    private float menuShowTime = 0f;                   // Time.time when menu was shown
    private const float MENU_INPUT_LOCKOUT = 1.5f;     // seconds to ignore input after menu appears
    private Text participantBannerText;                 // reference for late-update of banner

    // Colors for menu items
    private readonly Color colNormal          = new Color(0.18f, 0.20f, 0.30f, 0.92f);
    private readonly Color colHighlight       = new Color(0.30f, 0.42f, 0.82f, 0.96f);
    private readonly Color colCompleted       = new Color(0.14f, 0.32f, 0.16f, 0.92f);
    private readonly Color colCompletedHi     = new Color(0.22f, 0.52f, 0.30f, 0.96f);
    private readonly Color colTextNormal      = Color.white;
    private readonly Color colTextCompleted   = new Color(0.7f, 1f, 0.75f);

    // =========================================================================
    // UNITY LIFECYCLE
    // =========================================================================

    void Awake()
    {
        // CRITICAL: Completely disable the VideoPlayer to prevent ANY auto-play.
        // Unity's VideoPlayer.playOnAwake can fire during scene load (before Start).
        // We disable the entire component, then re-enable it in Start() after
        // we've properly configured everything and built the menu.
        if (videoPlayer != null)
        {
            videoPlayer.playOnAwake = false;
            videoPlayer.Stop();
            videoPlayer.url = "";
            videoPlayer.enabled = false;  // Fully disable until Start() configures it
            Debug.Log("[VideoManager] Awake: VideoPlayer DISABLED to prevent auto-play.");
        }
    }

    void Start()
    {
        // ----- Validate configuration -----
        if (videoPlayer == null)
        {
            Debug.LogError("[VideoManager] VideoPlayer not assigned!");
            return;
        }

        if (dataCollector == null)
        {
            Debug.LogError("[VideoManager] DataCollector not assigned!");
            return;
        }

        if (videoURLs.Count == 0)
        {
            Debug.LogWarning("[VideoManager] No videos configured! Add URLs in Inspector.");
            SetupDefaultVideos();
        }

        if (videoURLs.Count != videoIDs.Count)
        {
            Debug.LogWarning($"[VideoManager] Mismatch: {videoURLs.Count} URLs but {videoIDs.Count} IDs — auto-filling.");
            while (videoIDs.Count < videoURLs.Count)
                videoIDs.Add($"video_{videoIDs.Count}");
        }

        // ----- Configure video player -----
        videoPlayer.source = VideoSource.Url;
        videoPlayer.playOnAwake = false;
        videoPlayer.renderMode = VideoRenderMode.MaterialOverride;
        videoPlayer.skipOnDrop = true;
        videoPlayer.isLooping = false;

        // FIX: Ensure material property is set to _MainTex
        if (string.IsNullOrEmpty(videoPlayer.targetMaterialProperty))
        {
            videoPlayer.targetMaterialProperty = "_MainTex";
            Debug.Log("[VideoManager] Set targetMaterialProperty to _MainTex");
        }

        // FIX: Ensure shader is Unlit/Texture (not Standard) for proper 360° rendering
        if (videoPlayer.targetMaterialRenderer != null)
        {
            Material mat = videoPlayer.targetMaterialRenderer.material;
            if (mat != null && mat.shader.name == "Standard")
            {
                Shader unlitShader = Shader.Find("Unlit/Texture");
                if (unlitShader != null)
                {
                    mat.shader = unlitShader;
                    Debug.Log("[VideoManager] Changed shader from Standard to Unlit/Texture");
                }
            }
        }

        // ----- Register event callbacks -----
        videoPlayer.prepareCompleted += OnVideoPrepared;
        videoPlayer.errorReceived += OnVideoError;
        videoPlayer.loopPointReached += OnVideoFinished;

        Debug.Log($"[VideoManager] Configured with {videoURLs.Count} videos.");

        // FIX: Remap video URLs to app data path (avoids Android storage permission issues)
        RemapVideoURLsToAppData();

        // Request storage permission as fallback
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.ExternalStorageRead))
        {
            Permission.RequestUserPermission(Permission.ExternalStorageRead);
        }
#endif

        // ----- Re-enable VideoPlayer now that everything is configured -----
        videoPlayer.enabled = true;
        Debug.Log("[VideoManager] VideoPlayer re-enabled after configuration.");

        // ----- Build and show video selection menu -----
        BuildVideoMenu();
        ShowMenu();
    }

    void Update()
    {
        // ---- SAFETY: If menu should be visible but video is somehow playing, force-stop ----
        if (menuVisible && videoPlayer != null && videoPlayer.isPlaying && !isTransitioning)
        {
            Debug.LogWarning("[VideoManager] Video playing while menu is visible! Force-stopping.");
            videoPlayer.Stop();
        }

        // ---- When menu is visible, handle navigation ----
        if (menuVisible)
        {
            HandleMenuInput();
            return;
        }

        // ---- Backup video-end detection (loopPointReached doesn't always fire) ----
        if (videoPlayer != null && !isTransitioning &&
            currentVideoIndex >= 0 && !videoPreparing)
        {
            if (videoPlayer.isPrepared && !videoPlayer.isPlaying &&
                videoPlayer.time > 1.0 && videoPlayer.length > 0 &&
                videoPlayer.time >= videoPlayer.length - 0.5)
            {
                if (!videoEndDetected)
                {
                    videoEndDetected = true;
                    Debug.Log($"[VideoManager] Video end detected via Update() for index {currentVideoIndex}");
                    OnVideoFinished(videoPlayer);
                }
            }
        }
    }

    void OnDestroy()
    {
        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -= OnVideoPrepared;
            videoPlayer.errorReceived -= OnVideoError;
            videoPlayer.loopPointReached -= OnVideoFinished;
        }
    }

    // =========================================================================
    // VIDEO SELECTION MENU — BUILD
    // =========================================================================

    /// <summary>
    /// Programmatically creates a World Space Canvas with one button per video.
    /// No manual UI setup required — everything is generated at runtime.
    /// </summary>
    private void BuildVideoMenu()
    {
        // ---- Canvas ----
        menuRoot = new GameObject("VideoSelectionMenu");
        Canvas canvas = menuRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        CanvasScaler scaler = menuRoot.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10;
        menuRoot.AddComponent<GraphicRaycaster>(); // for potential pointer use

        RectTransform canvasRect = menuRoot.GetComponent<RectTransform>();
        float canvasW = 900f;
        float canvasH = 160f + videoURLs.Count * 62f + 80f; // banner + title + items + footer
        canvasRect.sizeDelta = new Vector2(canvasW, canvasH);
        canvasRect.localScale = Vector3.one * menuScale;

        // ---- Background panel ----
        GameObject bg = CreateUIObject("Background", menuRoot.transform);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.06f, 0.06f, 0.14f, 0.96f);
        StretchFill(bg);

        // ---- Participant ID banner (so user can verify the active ID) ----
        // Note: At build time, participantID may still be the Inspector default.
        // ShowMenu() will refresh this text with the actual loaded ID.
        string activeParticipantID = dataCollector != null ? dataCollector.participantID : "???";
        GameObject pidBanner = CreateTextObject("ParticipantBanner", bg.transform,
            $"Participant: {activeParticipantID}", 28,
            new Color(1f, 0.85f, 0.3f), TextAnchor.MiddleCenter);
        participantBannerText = pidBanner.GetComponent<Text>(); // store reference for late-update
        RectTransform pidRT = pidBanner.GetComponent<RectTransform>();
        pidRT.anchorMin = new Vector2(0, 1);
        pidRT.anchorMax = new Vector2(1, 1);
        pidRT.pivot = new Vector2(0.5f, 1);
        pidRT.anchoredPosition = new Vector2(0, -6);
        pidRT.sizeDelta = new Vector2(0, 36);

        // ---- Title ----
        GameObject title = CreateTextObject("Title", bg.transform,
            "— SELECT A VIDEO —", 34, Color.white, TextAnchor.MiddleCenter);
        RectTransform titleRT = title.GetComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0, 1);
        titleRT.anchorMax = new Vector2(1, 1);
        titleRT.pivot = new Vector2(0.5f, 1);
        titleRT.anchoredPosition = new Vector2(0, -42);
        titleRT.sizeDelta = new Vector2(0, 60);

        // ---- Video items ----
        float itemH = 52f;
        float gap = 10f;
        float startY = -115f; // shifted down to account for participant ID banner

        itemBackgrounds.Clear();
        itemTexts.Clear();

        for (int i = 0; i < videoURLs.Count; i++)
        {
            // Item panel
            GameObject item = CreateUIObject($"Item_{i}", bg.transform);
            Image itemBg = item.AddComponent<Image>();
            itemBg.color = colNormal;
            RectTransform itemRT = item.GetComponent<RectTransform>();
            itemRT.anchorMin = new Vector2(0, 1);
            itemRT.anchorMax = new Vector2(1, 1);
            itemRT.pivot = new Vector2(0.5f, 1);
            itemRT.anchoredPosition = new Vector2(0, startY - i * (itemH + gap));
            itemRT.sizeDelta = new Vector2(-40, itemH); // 20px margin each side

            // Item text
            string label = $"  {i + 1}.  {GetVideoDisplayName(i)}";
            GameObject txt = CreateTextObject($"Text_{i}", item.transform,
                label, 26, colTextNormal, TextAnchor.MiddleLeft);
            StretchFill(txt, 16, 16);

            itemBackgrounds.Add(itemBg);
            itemTexts.Add(txt.GetComponent<Text>());
        }

        // ---- Instructions footer ----
        GameObject footer = CreateTextObject("Footer", bg.transform,
            "▲▼  Thumbstick to navigate     |     A / Trigger to play",
            20, new Color(0.6f, 0.6f, 0.75f), TextAnchor.MiddleCenter);
        RectTransform footRT = footer.GetComponent<RectTransform>();
        footRT.anchorMin = new Vector2(0, 0);
        footRT.anchorMax = new Vector2(1, 0);
        footRT.pivot = new Vector2(0.5f, 0);
        footRT.anchoredPosition = new Vector2(0, 10);
        footRT.sizeDelta = new Vector2(0, 50);

        menuRoot.SetActive(false);
        UpdateMenuHighlight();

        Debug.Log($"[VideoManager] Video selection menu built ({videoURLs.Count} items).");
    }

    // =========================================================================
    // VIDEO SELECTION MENU — SHOW / HIDE / INPUT
    // =========================================================================

    /// <summary>Shows the menu, positioned in front of the camera.</summary>
    private void ShowMenu()
    {
        if (menuRoot == null) return;

        // Stop any playing video
        if (videoPlayer.isPlaying)
            videoPlayer.Stop();

        // Darken the sphere so the menu is readable
        SetSphereColor(new Color(0.05f, 0.05f, 0.05f));

        // Position in front of camera
        Transform cam = Camera.main != null ? Camera.main.transform : null;
        if (cam != null)
        {
            Vector3 fwd = cam.forward;
            fwd.y = 0; // keep menu at eye level
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();

            menuRoot.transform.position = cam.position + fwd * menuDistance;
            menuRoot.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        }
        else
        {
            menuRoot.transform.position = new Vector3(0, 1.5f, menuDistance);
            menuRoot.transform.rotation = Quaternion.identity;
        }

        UpdateMenuHighlight();
        menuRoot.SetActive(true);
        menuVisible = true;
        thumbstickCooldown = true;  // start with cooldown to avoid immediate input
        menuShowTime = Time.time;   // record when menu appeared (for input lockout)

        // Update participant banner in case DataCollector loaded a config file after menu was built
        if (participantBannerText != null && dataCollector != null)
            {
            participantBannerText.text = $"Participant: {dataCollector.participantID}";
        }

        Debug.Log("[VideoManager] Menu shown. Input locked for " + MENU_INPUT_LOCKOUT + "s.");
    }

    /// <summary>Hides the menu.</summary>
    private void HideMenu()
    {
        if (menuRoot == null) return;
        menuRoot.SetActive(false);
        menuVisible = false;

        // Restore sphere to white so video renders properly
        SetSphereColor(Color.white);
            }

    /// <summary>Process controller / keyboard input for menu navigation.</summary>
    private void HandleMenuInput()
    {
        // ---- INPUT LOCKOUT: Ignore all input for MENU_INPUT_LOCKOUT seconds ----
        // This prevents false-positive triggers/buttons from auto-selecting a video
        // the moment the menu appears.
        float timeSinceMenuShown = Time.time - menuShowTime;
        if (timeSinceMenuShown < MENU_INPUT_LOCKOUT)
            return;

        // ---- Read thumbstick (either hand) ----
        Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick)
                      + OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);

        // ONLY use A button and X button for selection.
        // Triggers are NOT used because resting finger pressure causes false positives.
        bool selectPressed = OVRInput.GetDown(OVRInput.Button.One)   // A button (right)
                          || OVRInput.GetDown(OVRInput.Button.Three); // X button (left)

        // ---- Keyboard fallback for Unity Editor ----
#if UNITY_EDITOR
        if (Input.GetKeyDown(KeyCode.UpArrow))   { stick = Vector2.up;   thumbstickCooldown = false; }
        if (Input.GetKeyDown(KeyCode.DownArrow)) { stick = Vector2.down; thumbstickCooldown = false; }
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
            selectPressed = true;
#endif

        // ---- Navigate up/down ----
        if (stick.y > 0.5f && !thumbstickCooldown)
        {
            selectedMenuIndex = Mathf.Max(0, selectedMenuIndex - 1);
            thumbstickCooldown = true;
            UpdateMenuHighlight();
        }
        else if (stick.y < -0.5f && !thumbstickCooldown)
        {
            selectedMenuIndex = Mathf.Min(videoURLs.Count - 1, selectedMenuIndex + 1);
            thumbstickCooldown = true;
            UpdateMenuHighlight();
        }

        if (Mathf.Abs(stick.y) < 0.3f)
            thumbstickCooldown = false;

        // ---- Select ----
        if (selectPressed)
        {
            Debug.Log($"[VideoManager] User selected video index {selectedMenuIndex} (time since menu: {timeSinceMenuShown:F1}s)");
            PlayVideoByIndex(selectedMenuIndex);
        }
    }

    /// <summary>Update visual highlight and completion marks.</summary>
    private void UpdateMenuHighlight()
    {
        for (int i = 0; i < itemBackgrounds.Count; i++)
        {
            bool done = completedVideos.Contains(i);
            bool selected = (i == selectedMenuIndex);

            if (done && selected)       itemBackgrounds[i].color = colCompletedHi;
            else if (done)              itemBackgrounds[i].color = colCompleted;
            else if (selected)          itemBackgrounds[i].color = colHighlight;
            else                        itemBackgrounds[i].color = colNormal;

            // Update text
            string check = done ? "  ✓" : "";
            string arrow = selected ? "►  " : "    ";
            itemTexts[i].text = $"{arrow}{i + 1}.  {GetVideoDisplayName(i)}{check}";
            itemTexts[i].color = done ? colTextCompleted : colTextNormal;
        }
        }

    // =========================================================================
    // VIDEO PLAYBACK
    // =========================================================================

    /// <summary>
    /// Plays a specific video by index. Called when user selects from the menu.
    /// </summary>
    public void PlayVideoByIndex(int index)
    {
        if (index < 0 || index >= videoURLs.Count)
        {
            Debug.LogWarning($"[VideoManager] Invalid video index: {index}");
            return;
        }

        HideMenu();
        currentVideoIndex = index;
        videoEndDetected = false;
        StartCoroutine(PrepareAndPlayVideo(index));
    }

    /// <summary>
    /// Coroutine: prepares and plays the video at the given index.
    /// </summary>
    private IEnumerator PrepareAndPlayVideo(int index)
    {
        isTransitioning = true;

        // Notify data collector
        string vid = videoIDs[index];
        if (dataCollector != null)
        {
            dataCollector.PrepareForNewVideo(vid);
            dataCollector.recordData = true;
        }

        // Load video
        string url = videoURLs[index];
        Debug.Log($"[VideoManager] Loading video {index + 1}/{videoURLs.Count}: {vid}");
        Debug.Log($"[VideoManager] URL: {url}");

        videoPreparing = true;
        videoPlayer.url = url;
        videoPlayer.Prepare();

        // Wait for preparation (with timeout)
        float timeout = 30f;
        float start = Time.time;
        while (!videoPlayer.isPrepared && (Time.time - start) < timeout)
            yield return null;

        if (!videoPlayer.isPrepared)
        {
            Debug.LogError($"[VideoManager] Preparation timed out for: {url}");
            videoPreparing = false;
            isTransitioning = false;
            ShowMenu(); // go back to menu on failure
            yield break;
        }

        videoPreparing = false;
        isTransitioning = false;
        // OnVideoPrepared callback will call Play()
    }

    // =========================================================================
    // EVENT CALLBACKS
    // =========================================================================

    private void OnVideoPrepared(VideoPlayer vp)
    {
        Debug.Log($"[VideoManager] Video prepared: {videoIDs[currentVideoIndex]} " +
                  $"(Duration: {vp.length:F1}s, Resolution: {vp.width}x{vp.height})");
        vp.Play();
    }

    private void OnVideoFinished(VideoPlayer vp)
    {
        if (isTransitioning) return;

        Debug.Log($"[VideoManager] Video finished: {videoIDs[currentVideoIndex]} " +
                  $"(Played: {vp.time:F1}s / {vp.length:F1}s)");

        // Mark completed
        completedVideos.Add(currentVideoIndex);

        // Stop recording
        if (dataCollector != null)
            dataCollector.StopRecording();

        // Return to menu after a brief pause
        StartCoroutine(ReturnToMenuAfterDelay(1.5f));
    }

    private void OnVideoError(VideoPlayer vp, string message)
    {
        Debug.LogError($"[VideoManager] Video error: {message}");
        Debug.LogError($"[VideoManager] Failed URL: {vp.url}");

        // Return to menu on error
        if (!isTransitioning)
            StartCoroutine(ReturnToMenuAfterDelay(1.0f));
    }

    private IEnumerator ReturnToMenuAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        ShowMenu();
    }

    // =========================================================================
    // URL REMAPPING (permission fix)
    // =========================================================================

    /// <summary>
    /// Remap video URLs from /sdcard/Movies/VRStudy/ to Application.persistentDataPath/Videos/
    /// This avoids Android storage permission issues on Quest Pro (Android 12+).
    /// </summary>
    private void RemapVideoURLsToAppData()
    {
        string appVideoPath = Path.Combine(Application.persistentDataPath, "Videos");

        if (!Directory.Exists(appVideoPath))
        {
            Debug.LogWarning($"[VideoManager] App video directory not found: {appVideoPath}");
            return;
        }

        int remapped = 0;
        for (int i = 0; i < videoURLs.Count; i++)
        {
            string filename = Path.GetFileName(videoURLs[i]);
            string localPath = Path.Combine(appVideoPath, filename);

            if (File.Exists(localPath))
            {
                videoURLs[i] = "file://" + localPath;
                remapped++;
            }
            else
            {
                Debug.LogWarning($"[VideoManager] Video not in app data: {filename} (keeping original URL)");
        }
        }

        Debug.Log($"[VideoManager] Remapped {remapped}/{videoURLs.Count} videos to app data path.");
    }

    // =========================================================================
    // DEFAULT VIDEO SETUP
    // =========================================================================

    private void SetupDefaultVideos()
    {
        string basePath = "file:///sdcard/Movies/VRStudy/";

        string[] defaultVideos = new string[]
        {
            "video_0.mp4", "video_1.mp4", "video_2.mp4",
            "video_3.mp4", "video_4.mp4", "video_5.mp4",
            "video_6.mp4", "video_7.mp4", "video_8.mp4"
        };

        videoURLs.Clear();
        videoIDs.Clear();

        for (int i = 0; i < defaultVideos.Length; i++)
        {
            videoURLs.Add(basePath + defaultVideos[i]);
            videoIDs.Add($"video_{i}");
        }

        Debug.Log($"[VideoManager] Setup {videoURLs.Count} default videos.");
    }

    // =========================================================================
    // HELPER: UI CONSTRUCTION
    // =========================================================================

    private Font _cachedFont;
    private Font DefaultFont
    {
        get
        {
            if (_cachedFont == null)
            {
                _cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_cachedFont == null)
                    _cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                if (_cachedFont == null)
                    _cachedFont = Font.CreateDynamicFontFromOSFont("Arial", 14);
            }
            return _cachedFont;
        }
    }

    private GameObject CreateUIObject(string name, Transform parent)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        return obj;
    }

    private GameObject CreateTextObject(string name, Transform parent,
        string text, int fontSize, Color color, TextAnchor alignment)
    {
        GameObject obj = CreateUIObject(name, parent);
        Text t = obj.AddComponent<Text>();
        t.text = text;
        t.font = DefaultFont;
        t.fontSize = fontSize;
        t.color = color;
        t.alignment = alignment;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        obj.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.5f); // readability
        return obj;
    }

    /// <summary>Stretch a RectTransform to fill its parent with optional insets.</summary>
    private void StretchFill(GameObject obj, float insetX = 0, float insetY = 0)
    {
        RectTransform rt = obj.GetComponent<RectTransform>();
        if (rt == null) rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(insetX, insetY);
        rt.offsetMax = new Vector2(-insetX, -insetY);
    }

    /// <summary>Formats a video ID for display in the menu.</summary>
    private string GetVideoDisplayName(int index)
    {
        if (index < 0 || index >= videoIDs.Count) return "???";

        string id = videoIDs[index];
        // Clean up underscores for display
        return id.Replace("_", " ");
    }

    /// <summary>Set the sphere material's base color (used for darkening during menu).</summary>
    private void SetSphereColor(Color color)
    {
        if (videoPlayer != null && videoPlayer.targetMaterialRenderer != null)
        {
            Material mat = videoPlayer.targetMaterialRenderer.material;
            if (mat != null)
                mat.color = color;
        }
    }

    // =========================================================================
    // PUBLIC UTILITY METHODS
    // =========================================================================

    public string GetProgressInfo()
    {
        if (menuVisible) return $"Menu — {completedVideos.Count}/{videoURLs.Count} completed";
        if (currentVideoIndex < 0) return "Not started";
        return $"Video {currentVideoIndex + 1}/{videoURLs.Count}: {videoIDs[currentVideoIndex]}";
    }

    public int GetTotalVideos() => videoURLs.Count;
    public int GetCurrentVideoIndex() => currentVideoIndex;
    public bool IsMenuVisible() => menuVisible;
    public int GetCompletedCount() => completedVideos.Count;
}
