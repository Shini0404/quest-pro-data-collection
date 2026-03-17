// =============================================================================
// ParticipantSetup.cs
// Purpose: Simple VR UI to enter participant ID before data collection starts
// Attach to: Canvas GameObject in ParticipantSetup scene
// Project: STAR-VP Quest Pro Data Collection
// Unity: 2022.3 LTS
//
// SCENE SETUP:
//   1. Create new scene: "ParticipantSetup"
//   2. Add OVRCameraRig (same as DataCollection scene)
//   3. Add World Space Canvas (for VR):
//      - Hierarchy → UI → Canvas
//      - Canvas → Render Mode: "World Space"
//      - Rect Transform: Position (0, 1.5, 2), Width 800, Height 600, Scale (0.002, 0.002, 0.002)
//   4. Add UI elements:
//      - InputField (TMP or Legacy) for participant ID
//      - Button to start collection
//   5. Attach this script to Canvas
//   6. Wire up references in Inspector
//
// ALTERNATIVE (SIMPLER):
//   Skip this scene entirely and set participant ID directly
//   in the DataCollector Inspector before building.
//   For research with few participants, this is often easier.
// =============================================================================

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class ParticipantSetup : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Input field for participant ID")]
    public InputField participantIDInput;

    [Tooltip("Start button")]
    public Button startButton;

    [Tooltip("Status text (optional)")]
    public Text statusText;

    [Header("Settings")]
    [Tooltip("Name of the data collection scene to load")]
    public string dataCollectionSceneName = "DataCollection";

    void Start()
    {
        // Set default text
        if (participantIDInput != null)
        {
            participantIDInput.text = "";
            participantIDInput.placeholder.GetComponent<Text>().text = "Enter ID (e.g., P001)";
        }

        // Wire up button click
        if (startButton != null)
        {
            startButton.onClick.AddListener(OnStartClicked);
        }

        // Load last used participant ID for convenience
        string lastID = PlayerPrefs.GetString("LastParticipantID", "");
        if (!string.IsNullOrEmpty(lastID) && statusText != null)
        {
            statusText.text = $"Last participant: {lastID}";
        }
    }

    public void OnStartClicked()
    {
        if (participantIDInput == null)
        {
            Debug.LogError("[ParticipantSetup] Input field not assigned!");
            return;
        }

        string participantID = participantIDInput.text.Trim();

        // Validate input
        if (string.IsNullOrEmpty(participantID))
        {
            SetStatus("ERROR: Please enter a participant ID!", Color.red);
            Debug.LogError("[ParticipantSetup] Empty participant ID!");
            return;
        }

        // Basic validation: should start with P and be 3-6 chars
        if (participantID.Length < 2 || participantID.Length > 10)
        {
            SetStatus("ERROR: ID should be 2-10 characters (e.g., P001)", Color.red);
            return;
        }

        // Save participant ID for DataCollector to read
        PlayerPrefs.SetString("ParticipantID", participantID);
        PlayerPrefs.SetString("LastParticipantID", participantID);
        PlayerPrefs.Save();

        Debug.Log($"[ParticipantSetup] Participant ID set: {participantID}");
        SetStatus($"Starting collection for {participantID}...", Color.green);

        // Load data collection scene
        SceneManager.LoadScene(dataCollectionSceneName);
    }

    private void SetStatus(string message, Color color)
    {
        if (statusText != null)
        {
            statusText.text = message;
            statusText.color = color;
        }
        Debug.Log($"[ParticipantSetup] {message}");
    }
}
