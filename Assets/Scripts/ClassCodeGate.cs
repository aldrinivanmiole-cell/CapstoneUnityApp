using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using UnityEngine.Networking;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using System.Linq;

public enum GameplayType
{
    MultipleChoice,
    Enumeration,
    FillInBlank,
    YesNo
}

[System.Serializable]
public class JoinClassResponse
{
    public string subject;
    public string gameplay_type;
}

[System.Serializable]
public class JoinClassPayload
{
    public string class_code;
    public int student_id;
}

public class ClassCodeGate : MonoBehaviour
{
    [Header("Class Code UI")]
    public GameObject classCodePanel;
    public TMP_InputField classCodeInput;
    public Button submitButton;
    public Button skipButton;
    public Button cancelButton;
    public Button showClassCodeButton;

    [Header("Subjects UI")]
    public GameObject subjectButtonsContainer; // The scrollable container for dynamic subject buttons
    public GameObject subjectButtonPrefab; // Prefab for creating all subject buttons dynamically (OPTIONAL - will use existing buttons if not set)

    [Header("Stage Panel Integration")]
    public GameObject stagePanel;
    public Button[] stagePanelButtons;
    public TMP_Text[] stagePanelButtonTexts;
    public Button backButtonFromStagePanel;

    [Header("Status Display")]
    public TMP_Text statusText;
    public TMP_Text studentNameText;
    public TMP_Text currentClassesText;

    [Header("Classroom Integration")]
    public bool enableClassroomMode = true;
    public string serverURL = "https://homequest-c3k7.onrender.com";

    private const string CLASS_CODE_KEY = "ClassCodeEntered";
    private List<string> unlockedStages = new List<string>();
    private List<string> joinedSubjects = new List<string>();
    private Dictionary<string, GameplayType> subjectGameplayTypes = new Dictionary<string, GameplayType>();
    private List<Button> staticSubjectButtons = new List<Button>();

    void Start()
    {
        // Clear any invalid/old data first
        CleanupInvalidPlayerPrefs();

        SetupClassCodeGate();
        FindStaticSubjectButtons();
        InitializeStagePanel();
        // Load unlocked stages after finding buttons to ensure proper initial state
        LoadUnlockedStages();

        // Debug: Show initial state
        Debug.Log($"Initial joined classes: {PlayerPrefs.GetString("JoinedClasses", "NONE")}");
        Debug.Log($"Server enabled: {enableClassroomMode}, Student logged in: {IsStudentLoggedIn()}");
    }

    void CleanupInvalidPlayerPrefs()
    {
        // Force clear ALL class data to start fresh
        Debug.Log("FORCING COMPLETE CLEANUP OF ALL CLASS DATA");

        // Get current data first for logging
        string oldData = PlayerPrefs.GetString("JoinedClasses", "");
        Debug.Log($"OLD DATA TO BE CLEARED: '{oldData}'");

        // Delete ALL class-related keys
        PlayerPrefs.DeleteKey("JoinedClasses");
        PlayerPrefs.DeleteKey("LastClassCode");
        PlayerPrefs.DeleteKey(CLASS_CODE_KEY);

        // Delete all possible stage keys (even invalid ones)
        if (!string.IsNullOrEmpty(oldData))
        {
            string[] oldClasses = oldData.Split(',');
            foreach (string className in oldClasses)
            {
                if (!string.IsNullOrWhiteSpace(className))
                {
                    PlayerPrefs.DeleteKey($"Stage_{className}_GameplayType");
                    PlayerPrefs.DeleteKey($"Stage_{className}_Unlocked");
                    Debug.Log($"Deleted keys for: {className}");
                }
            }
        }

        // Also clear some common invalid keys that might exist
        string[] commonInvalidNames = { "UJYOGTZ", "XJBWKO", "gay", "test", "drugs" };
        foreach (string invalidName in commonInvalidNames)
        {
            PlayerPrefs.DeleteKey($"Stage_{invalidName}_GameplayType");
            PlayerPrefs.DeleteKey($"Stage_{invalidName}_Unlocked");
        }

        PlayerPrefs.Save();
        Debug.Log("✓ COMPLETE CLEANUP FINISHED - All class data cleared");

        // Verify cleanup worked
        string verifyData = PlayerPrefs.GetString("JoinedClasses", "");
        Debug.Log($"VERIFICATION - JoinedClasses after cleanup: '{verifyData}' (should be empty)");
    }

    void SetupClassCodeGate()
    {
        if (submitButton != null)
            submitButton.onClick.AddListener(SubmitClassCode);
        if (skipButton != null)
            skipButton.onClick.AddListener(SkipClassCode);
        if (cancelButton != null)
            cancelButton.onClick.AddListener(CancelClassCode);
        if (showClassCodeButton != null)
            showClassCodeButton.onClick.AddListener(ShowClassCodePanel);
        if (classCodePanel != null)
            classCodePanel.SetActive(false);

        if (enableClassroomMode)
            SetupClassroomIntegration();
        else
            SetupLegacyMode();
    }

    void FindStaticSubjectButtons()
    {
        // Find existing subject buttons in the container instead of destroying them
        staticSubjectButtons.Clear();

        if (subjectButtonsContainer != null)
        {
            // Look for existing subject buttons
            Button[] foundButtons = subjectButtonsContainer.GetComponentsInChildren<Button>();
            foreach (Button btn in foundButtons)
            {
                TMP_Text btnText = btn.GetComponentInChildren<TMP_Text>();
                if (btnText != null)
                {
                    string buttonText = btnText.text.Trim().ToUpper();
                    // If it's a subject button (not navigation), add it to our list
                    if (IsSubjectButton(buttonText) && btn != submitButton && btn != skipButton && btn != cancelButton && btn != showClassCodeButton)
                    {
                        staticSubjectButtons.Add(btn);
                        Debug.Log($"Found existing subject button: {buttonText}");
                    }
                }
            }
        }

        Debug.Log($"Found {staticSubjectButtons.Count} existing subject buttons");
    }

    bool IsSubjectButton(string buttonText)
    {
        // Define which buttons are navigation/UI buttons (NOT subjects)
        string[] nonSubjects = { "CLASS", "ADD", "MAP", "BACK", "NEXT", "HOME", "MENU", "SETTINGS", "JOIN", "SUBMIT", "CANCEL", "SKIP", "GRADE" };

        // Check if it's explicitly a non-subject button
        foreach (string nonSubject in nonSubjects)
        {
            if (buttonText.Contains(nonSubject))
                return false;
        }

        // If it's not a navigation button and has text, assume it's a subject button
        // This makes it dynamic - any button with text that's not a navigation button is treated as a subject
        return !string.IsNullOrWhiteSpace(buttonText);
    }

    void SetButtonLocked(Button btn, bool locked)
    {
        btn.interactable = !locked;
        TMP_Text btnText = btn.GetComponentInChildren<TMP_Text>();
        Image btnImage = btn.GetComponent<Image>();

        if (locked)
        {
            if (btnText != null)
                btnText.color = new Color(0.5f, 0.5f, 0.5f, 0.7f);
            if (btnImage != null)
                btnImage.color = new Color(0.7f, 0.7f, 0.7f, 0.7f);
        }
        else
        {
            if (btnText != null)
                btnText.color = Color.white;
            if (btnImage != null)
                btnImage.color = Color.white;
        }
    }

    void InitializeStagePanel()
    {
        if (stagePanel != null)
            stagePanel.SetActive(false);
        if (backButtonFromStagePanel != null)
            backButtonFromStagePanel.onClick.AddListener(HideStagePanel);

        // Initialize all stage panel buttons as hidden/transparent
        InitializeStagePanelButtons();
    }

    void InitializeStagePanelButtons()
    {
        if (stagePanelButtons == null) return;

        // Limit to maximum 7 stage panel buttons
        int maxButtons = Mathf.Min(stagePanelButtons.Length, 7);

        for (int i = 0; i < maxButtons; i++)
        {
            if (stagePanelButtons[i] != null)
            {
                // Make buttons transparent and non-interactable initially
                stagePanelButtons[i].interactable = false;
                stagePanelButtons[i].gameObject.SetActive(true); // Ensure it's visible for the limit

                // Set transparent appearance
                Image buttonImage = stagePanelButtons[i].GetComponent<Image>();
                if (buttonImage != null)
                    buttonImage.color = new Color(0.5f, 0.5f, 0.5f, 0.3f); // Very transparent

                // Set text to show empty state
                if (stagePanelButtonTexts != null && i < stagePanelButtonTexts.Length && stagePanelButtonTexts[i] != null)
                {
                    stagePanelButtonTexts[i].text = "Empty";
                    stagePanelButtonTexts[i].color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
                }

                stagePanelButtons[i].onClick.RemoveAllListeners();
            }
        }

        // Hide any extra buttons beyond the 7 limit
        for (int i = maxButtons; i < stagePanelButtons.Length; i++)
        {
            if (stagePanelButtons[i] != null)
            {
                stagePanelButtons[i].gameObject.SetActive(false);
            }
        }

        Debug.Log($"Stage panel initialized: {maxButtons} buttons available (max 7)");
    }

    void UpdateStagePanelButtons()
    {
        if (stagePanelButtons == null) return;

        // Only update if server is connected and student is logged in
        bool serverConnected = enableClassroomMode && IsStudentLoggedIn();

        // Limit to maximum 7 stage panel buttons
        int maxButtons = Mathf.Min(stagePanelButtons.Length, 7);

        for (int i = 0; i < maxButtons; i++)
        {
            if (stagePanelButtons[i] != null)
            {
                // Check if this button slot has a corresponding unlocked subject
                bool hasUnlockedStage = serverConnected && i < unlockedStages.Count;
                stagePanelButtons[i].interactable = hasUnlockedStage;

                if (hasUnlockedStage)
                {
                    // Dynamically assign the subject name to this button slot
                    string subjectName = unlockedStages[i];
                    if (stagePanelButtonTexts != null && i < stagePanelButtonTexts.Length && stagePanelButtonTexts[i] != null)
                    {
                        stagePanelButtonTexts[i].text = subjectName; // Could be "Biology", "History", "Japan Class", etc.
                        stagePanelButtonTexts[i].color = Color.white;
                    }
                    Image buttonImage = stagePanelButtons[i].GetComponent<Image>();
                    if (buttonImage != null)
                        buttonImage.color = Color.white;

                    // Capture the subject name for the button click
                    string capturedSubjectName = subjectName;
                    stagePanelButtons[i].onClick.RemoveAllListeners();
                    stagePanelButtons[i].onClick.AddListener(() => LoadSubjectScene(capturedSubjectName));

                    Debug.Log($"Stage button {i + 1} assigned to subject: {subjectName}");
                }
                else
                {
                    // This button slot is empty or no connection
                    if (stagePanelButtonTexts != null && i < stagePanelButtonTexts.Length && stagePanelButtonTexts[i] != null)
                    {
                        stagePanelButtonTexts[i].text = serverConnected ? "Empty" : "No Connection";
                        stagePanelButtonTexts[i].color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
                    }
                    Image buttonImage = stagePanelButtons[i].GetComponent<Image>();
                    if (buttonImage != null)
                        buttonImage.color = new Color(0.5f, 0.5f, 0.5f, 0.3f); // More transparent when no connection
                    stagePanelButtons[i].onClick.RemoveAllListeners();
                }
            }
        }

        // Hide any extra buttons beyond the 7 limit (if stagePanelButtons array is larger)
        for (int i = maxButtons; i < stagePanelButtons.Length; i++)
        {
            if (stagePanelButtons[i] != null)
            {
                stagePanelButtons[i].gameObject.SetActive(false);
            }
        }

        Debug.Log($"Stage panel updated: {Mathf.Min(unlockedStages.Count, 7)} buttons active out of {maxButtons} available");
    }

    public void ShowStagePanel()
    {
        if (stagePanel != null)
        {
            // Only show stage panel if server is connected and student is logged in
            if (enableClassroomMode && IsStudentLoggedIn() && unlockedStages.Count > 0)
            {
                stagePanel.SetActive(true);
                UpdateStagePanelButtons();
                Debug.Log($"Stage panel shown with {Mathf.Min(unlockedStages.Count, 7)} dynamic subjects (max 7)");
            }
            else
            {
                if (!IsStudentLoggedIn())
                    UpdateStatus("Please login first to access subjects", Color.red);
                else if (unlockedStages.Count == 0)
                    UpdateStatus("Join a class first to unlock subjects", Color.orange);
                else
                    UpdateStatus("Server connection required", Color.red);
                Debug.Log("Stage panel not shown - requirements not met");
            }
        }
    }

    public void HideStagePanel()
    {
        if (stagePanel != null)
            stagePanel.SetActive(false);
    }

    void LoadSubjectScene(string subjectName)
    {
        GameplayType type = GameplayType.MultipleChoice;
        if (subjectGameplayTypes.ContainsKey(subjectName))
            type = subjectGameplayTypes[subjectName];

        string sceneToLoad = GetGameplaySceneName(type);
        Debug.Log($"Loading scene for subject '{subjectName}' with type '{type}' -> scene: '{sceneToLoad}'");

        if (!string.IsNullOrEmpty(sceneToLoad))
        {
            UpdateStatus($"Loading {subjectName} ({type})...", Color.green);
            SceneManager.LoadScene(sceneToLoad);
        }
        else
        {
            UpdateStatus($"Scene not configured for {subjectName} ({type})", Color.red);
        }
    }

    string GetGameplaySceneName(GameplayType type)
    {
        switch (type)
        {
            case GameplayType.MultipleChoice: return "GameplayScene";
            case GameplayType.Enumeration: return "GPenumeration";
            case GameplayType.FillInBlank: return "GPfillblank";
            case GameplayType.YesNo: return "GPyesno";
            default: return "GameplayScene";
        }
    }

    void UpdateStatus(string message, Color color)
    {
        if (statusText != null)
        {
            statusText.text = message;
            statusText.color = color;
        }
        Debug.Log($"ClassCodeGate: {message}");
    }

    void SetupClassroomIntegration()
    {
        if (!IsStudentLoggedIn())
        {
            UpdateStatus("Please login first to join classes", Color.red);
            return;
        }
        string studentName = GetCurrentStudentName();
        ShowStudentInfo(studentName);
        if (HasExistingClasses())
        {
            ShowExistingClasses();
            UpdateStatus("Welcome back! Click 'Join Class' to add more subjects", Color.green);
        }
        else
        {
            UpdateStatus("Click 'Join Class' to enter class codes and unlock subjects", Color.blue);
        }
        // Always load unlocked stages to ensure proper button states
        LoadUnlockedStages();
    }

    void SetupLegacyMode()
    {
        if (PlayerPrefs.HasKey(CLASS_CODE_KEY))
        {
            if (subjectButtonsContainer != null)
                subjectButtonsContainer.SetActive(true);
        }
        UpdateStatus("Click 'Join Class' to enter subject codes", Color.gray);
    }

    public void ShowClassCodePanel()
    {
        if (classCodePanel != null)
        {
            classCodePanel.SetActive(true);
            UpdateStatus("Enter a class code to unlock a subject, or cancel to go back", Color.blue);
            if (classCodeInput != null)
            {
                classCodeInput.text = "";
                classCodeInput.ActivateInputField();
            }
            if (submitButton != null)
                submitButton.interactable = true;
            if (cancelButton != null)
                cancelButton.interactable = true;
        }
    }

    void SubmitClassCode()
    {
        string enteredCode = classCodeInput.text.Trim().ToUpper();
        if (string.IsNullOrEmpty(enteredCode))
        {
            UpdateStatus("Please enter a valid class code", Color.red);
            return;
        }
        if (enteredCode.Length < 3)
        {
            UpdateStatus("Class code must be at least 3 characters", Color.red);
            return;
        }

        // Enhanced validation for classroom mode
        if (enableClassroomMode)
        {
            int studentId = PlayerPrefs.GetInt("StudentID", 0);
            string studentName = GetCurrentStudentName();

            Debug.Log($"=== SUBMIT CLASS CODE VALIDATION ===");
            Debug.Log($"Class Code: '{enteredCode}'");
            Debug.Log($"Student ID: {studentId}");
            Debug.Log($"Student Name: '{studentName}'");
            Debug.Log($"Is Student Logged In: {IsStudentLoggedIn()}");

            if (studentId <= 0)
            {
                UpdateStatus("Invalid Student ID. Please login again.", Color.red);
                Debug.LogError("Student ID is invalid or not set");
                return;
            }

            if (!IsStudentLoggedIn())
            {
                UpdateStatus("Please login first before joining classes", Color.red);
                Debug.LogError("Student not properly logged in");
                return;
            }

            StartCoroutine(JoinClassWithAPI(enteredCode));
        }
        else
        {
            AcceptClassCode(enteredCode, GameplayType.MultipleChoice); // Default type for legacy
        }
    }

    IEnumerator JoinClassWithAPI(string classCode)
    {
        UpdateStatus("Joining class...", Color.yellow);
        if (submitButton != null)
            submitButton.interactable = false;
        if (cancelButton != null)
            cancelButton.interactable = false;

        string url = serverURL + "/student/join-class";
        int studentId = PlayerPrefs.GetInt("StudentID", 1);
        JoinClassPayload payload = new JoinClassPayload { class_code = classCode, student_id = studentId };
        string jsonData = JsonUtility.ToJson(payload);

        // Enhanced debugging - log the request details
        Debug.Log($"=== API REQUEST DEBUG ===");
        Debug.Log($"URL: {url}");
        Debug.Log($"Class Code: '{classCode}'");
        Debug.Log($"Student ID: {studentId}");
        Debug.Log($"JSON Payload: {jsonData}");
        Debug.Log($"Payload Size: {jsonData.Length} characters");

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        bool success = false;
        string subjectToUnlock = classCode;
        GameplayType gameplayTypeToSet = GameplayType.MultipleChoice;

        // Enhanced error logging
        Debug.Log($"=== API RESPONSE DEBUG ===");
        Debug.Log($"Response Code: {request.responseCode}");
        Debug.Log($"Request Result: {request.result}");
        Debug.Log($"Error (if any): {request.error}");
        Debug.Log($"Response Text: {request.downloadHandler.text}");

        if (request.result == UnityWebRequest.Result.Success && request.responseCode == 200)
        {
            string responseText = request.downloadHandler.text;
            Debug.Log($"✓ SUCCESS - Backend Response: {responseText}");
            JoinClassResponse response = JsonUtility.FromJson<JoinClassResponse>(responseText);
            subjectToUnlock = response.subject;
            gameplayTypeToSet = ParseGameplayType(response.gameplay_type);
            success = true;
        }
        else if (request.responseCode == 400)
        {
            string errorResponse = request.downloadHandler.text;
            Debug.LogError($"400 BAD REQUEST - Server Error Details: {errorResponse}");
            UpdateStatus($"Bad Request: {errorResponse}", Color.red);
        }
        else if (request.responseCode == 404)
        {
            UpdateStatus("Student not found. Please register first.", Color.red);
            Debug.LogError("404 - Student not found");
        }
        else
        {
            string errorDetails = !string.IsNullOrEmpty(request.downloadHandler.text) ? request.downloadHandler.text : request.error;
            UpdateStatus($"Failed to join class: {errorDetails}", Color.red);
            Debug.LogError($"API Error - Code: {request.responseCode}, Details: {errorDetails}");
        }

        request.Dispose();

        if (success)
        {
            UpdateStatus($"Successfully joined class: {subjectToUnlock}!", Color.green);
            AcceptClassCode(subjectToUnlock, gameplayTypeToSet);
            yield return new WaitForSeconds(1f);
            ShowExistingClasses();
            ShowJoinedClassesInUI();
            yield return new WaitForSeconds(2f);
            if (classCodePanel != null)
                classCodePanel.SetActive(false);
        }

        if (submitButton != null)
            submitButton.interactable = true;
        if (cancelButton != null)
            cancelButton.interactable = true;
        if (classCodeInput != null)
            classCodeInput.text = "";
    }

    GameplayType ParseGameplayType(string type)
    {
        string lowerType = type.ToLower().Trim();
        Debug.Log($"Parsing gameplay type: '{type}' -> '{lowerType}'");

        switch (lowerType)
        {
            case "multiplechoice":
            case "multiple choice":
            case "mcq":
                return GameplayType.MultipleChoice;
            case "enumeration":
            case "input":
            case "input field":
            case "text input":
                return GameplayType.Enumeration;
            case "fillinblank":
            case "fill in blank":
            case "fill_in_blank":
                return GameplayType.FillInBlank;
            case "yesno":
            case "yes no":
            case "yes/no":
            case "true false":
            case "truefalse":
                return GameplayType.YesNo;
            default:
                Debug.Log($"Unknown gameplay type '{type}', defaulting to MultipleChoice");
                return GameplayType.MultipleChoice;
        }
    }

    void AcceptClassCode(string classCode, GameplayType gameplayType)
    {
        string joinedClasses = PlayerPrefs.GetString("JoinedClasses", "");
        List<string> classes = new List<string>();
        if (!string.IsNullOrEmpty(joinedClasses))
            classes = new List<string>(joinedClasses.Split(','));

        if (!classes.Contains(classCode))
            classes.Add(classCode);

        PlayerPrefs.SetString("JoinedClasses", string.Join(",", classes));
        PlayerPrefs.SetString($"Stage_{classCode}_GameplayType", gameplayType.ToString());
        PlayerPrefs.Save();

        Debug.Log($"Saved class '{classCode}' with gameplay type '{gameplayType}'");
        UpdateStatus("Class joined! Enter another code or close to continue", Color.green);
        LoadUnlockedStages();
    }

    void LoadUnlockedStages()
    {
        unlockedStages.Clear();
        joinedSubjects.Clear();
        subjectGameplayTypes.Clear();

        string joinedClasses = PlayerPrefs.GetString("JoinedClasses", "");
        Debug.Log($"LoadUnlockedStages - JoinedClasses: '{joinedClasses}'");

        if (string.IsNullOrEmpty(joinedClasses))
        {
            Debug.Log("No joined classes found - starting with clean state");
            UpdateStaticSubjectButtons();
            UpdateStagePanelButtons();
            return;
        }

        string[] classes = joinedClasses.Split(',');
        foreach (string subjectName in classes)
        {
            if (string.IsNullOrWhiteSpace(subjectName)) continue;

            string cleanSubject = subjectName.Trim();
            Debug.Log($"Processing subject: '{cleanSubject}'");

            GameplayType type = ParseGameplayType(PlayerPrefs.GetString($"Stage_{cleanSubject}_GameplayType", "MultipleChoice"));
            subjectGameplayTypes[cleanSubject] = type;
            unlockedStages.Add(cleanSubject);
            joinedSubjects.Add(cleanSubject);

            Debug.Log($"✓ Added subject: '{cleanSubject}' with type: {type}");
        }

        // Update existing static buttons based on joined classes
        UpdateStaticSubjectButtons();

        // Update stage panel buttons
        UpdateStagePanelButtons();

        // Debug log the current state
        Debug.Log($"=== FINAL LOAD RESULTS ===");
        Debug.Log($"Total subjects loaded: {unlockedStages.Count}");
        for (int i = 0; i < unlockedStages.Count; i++)
        {
            Debug.Log($"  {i + 1}. {unlockedStages[i]} ({subjectGameplayTypes[unlockedStages[i]]})");
        }

        if (unlockedStages.Count == 0)
            Debug.Log("No subjects loaded - all buttons will be locked");
    }

    void ClearAllSubjectButtons()
    {
        // Reset all buttons to their original state
        string[] originalButtonNames = { "MATH", "ENG", "SCI", "PE", "ART" };

        for (int i = 0; i < staticSubjectButtons.Count; i++)
        {
            Button btn = staticSubjectButtons[i];
            if (btn != null)
            {
                TMP_Text btnText = btn.GetComponentInChildren<TMP_Text>();
                if (btnText != null && i < originalButtonNames.Length)
                {
                    btnText.text = originalButtonNames[i]; // Reset to original name
                }
                SetButtonLocked(btn, true); // Lock the button
                btn.onClick.RemoveAllListeners();
            }
        }
        Debug.Log("Reset all subject buttons to original locked state");
    }

    void EnsureSubjectButtonExists(string subjectName)
    {
        // This method is no longer needed since we recreate all buttons in LoadUnlockedStages
        // Keeping it for backward compatibility but it does nothing
        Debug.Log($"EnsureSubjectButtonExists called for {subjectName} - but all buttons are now fully dynamic");
    }

    void CreateDynamicSubjectButton(string subjectName, bool unlocked = false)
    {
        // This method is now optional - only used if prefab is assigned
        // Otherwise we work with existing static buttons
        Debug.Log($"CreateDynamicSubjectButton called for {subjectName} - using existing static buttons instead");
    }

    void UpdateStaticSubjectButtons()
    {
        // Update existing static buttons based on joined classes
        // If we have more joined subjects than static buttons, we'll show as many as possible

        for (int i = 0; i < staticSubjectButtons.Count; i++)
        {
            Button btn = staticSubjectButtons[i];
            if (btn == null) continue;

            TMP_Text btnText = btn.GetComponentInChildren<TMP_Text>();
            if (btnText == null) continue;

            // Check if we have a subject to assign to this button slot
            if (i < joinedSubjects.Count)
            {
                // Assign the joined subject name to this button
                string subjectName = joinedSubjects[i];
                btnText.text = subjectName; // Change button text to the actual subject name

                SetButtonLocked(btn, false); // Unlock the button

                // Add click listener to show stage panel
                string capturedSubjectName = subjectName; // Capture for lambda
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => {
                    Debug.Log($"Subject button clicked: {capturedSubjectName}");
                    ShowStagePanel();
                });
                Debug.Log($"Button {i + 1} assigned to subject: {subjectName}");
            }
            else
            {
                // No more subjects to assign - keep original text but lock the button
                SetButtonLocked(btn, true);
                btn.onClick.RemoveAllListeners();
                Debug.Log($"Button {i + 1} locked (no subject assigned)");
            }
        }

        Debug.Log($"Updated {staticSubjectButtons.Count} buttons with {joinedSubjects.Count} subjects");
    }

    void SkipClassCode()
    {
        if (classCodePanel != null)
            classCodePanel.SetActive(false);
        UpdateStatus("Enter class codes to unlock more subjects", Color.blue);
    }

    void CancelClassCode()
    {
        if (classCodePanel != null)
            classCodePanel.SetActive(false);
        if (classCodeInput != null)
            classCodeInput.text = "";
        if (HasExistingClasses())
            UpdateStatus("Welcome back! Click 'Join Class' when ready to add more subjects", Color.green);
        else
            UpdateStatus("Click 'Join Class' when you're ready to enter class codes", Color.blue);
    }

    bool IsStudentLoggedIn()
    {
        bool hasStudentName = !string.IsNullOrEmpty(PlayerPrefs.GetString("StudentName", ""));
        bool hasLoggedInUser = !string.IsNullOrEmpty(PlayerPrefs.GetString("LoggedInUser", ""));
        bool isLoggedInFlag = PlayerPrefs.GetInt("IsLoggedIn", 0) == 1;
        bool hasStudentID = PlayerPrefs.GetInt("StudentID", 0) > 0;
        return hasStudentName || hasLoggedInUser || isLoggedInFlag || hasStudentID;
    }

    string GetCurrentStudentName()
    {
        string studentName = PlayerPrefs.GetString("StudentName", "");
        if (string.IsNullOrEmpty(studentName))
            studentName = PlayerPrefs.GetString("LoggedInUser", "");
        if (string.IsNullOrEmpty(studentName))
            studentName = "Logged In Student";
        return studentName;
    }

    bool HasExistingClasses()
    {
        string joinedClasses = PlayerPrefs.GetString("JoinedClasses", "");
        return !string.IsNullOrEmpty(joinedClasses);
    }

    void ShowExistingClasses()
    {
        string joinedClasses = PlayerPrefs.GetString("JoinedClasses", "");
        if (!string.IsNullOrEmpty(joinedClasses))
        {
            string[] classes = joinedClasses.Split(',');
            string classDisplay = "Your Classes:\n";
            foreach (string className in classes)
                if (!string.IsNullOrWhiteSpace(className))
                    classDisplay += $"• {className}\n";
            if (currentClassesText != null)
                currentClassesText.text = classDisplay;
            UpdateStatus($"You are in {classes.Length} class(es)", Color.green);
        }
    }

    void ShowStudentInfo(string studentName)
    {
        if (studentNameText != null)
            studentNameText.text = $"Student: {studentName}";
    }

    public void SetServerURL(string url)
    {
        serverURL = url;
    }

    public void SetStudentName(string name)
    {
        PlayerPrefs.SetString("StudentName", name);
        PlayerPrefs.Save();
    }

    public string[] GetJoinedClasses()
    {
        string joinedClasses = PlayerPrefs.GetString("JoinedClasses", "");
        if (string.IsNullOrEmpty(joinedClasses))
            return new string[0];
        return joinedClasses.Split(',');
    }

    public void ClearClassData()
    {
        Debug.Log("=== MANUAL CLEAR CLASS DATA CALLED ===");

        // Get current data for logging
        string currentData = PlayerPrefs.GetString("JoinedClasses", "");
        Debug.Log($"Current JoinedClasses before clear: '{currentData}'");

        // Clear everything
        PlayerPrefs.DeleteKey(CLASS_CODE_KEY);
        PlayerPrefs.DeleteKey("JoinedClasses");
        PlayerPrefs.DeleteKey("LastClassCode");

        // Clear all possible stage gameplay type keys
        if (!string.IsNullOrEmpty(currentData))
        {
            string[] classes = currentData.Split(',');
            foreach (string className in classes)
            {
                if (!string.IsNullOrWhiteSpace(className))
                {
                    PlayerPrefs.DeleteKey($"Stage_{className}_Unlocked");
                    PlayerPrefs.DeleteKey($"Stage_{className}_GameplayType");
                    Debug.Log($"Cleared keys for: {className}");
                }
            }
        }

        // Also clear from memory
        unlockedStages.Clear();
        joinedSubjects.Clear();
        subjectGameplayTypes.Clear();

        PlayerPrefs.Save();

        // Reset buttons to original state
        ClearAllSubjectButtons();

        // Reinitialize everything
        UpdateStagePanelButtons();
        LoadUnlockedStages();

        UpdateStatus("✓ ALL CLASS DATA CLEARED - Ready for fresh enrollment", Color.blue);
        Debug.Log("=== MANUAL CLEAR COMPLETED ===");

        // Verify it worked
        string verifyAfter = PlayerPrefs.GetString("JoinedClasses", "");
        Debug.Log($"VERIFICATION: JoinedClasses after manual clear: '{verifyAfter}' (should be empty)");
    }

    // Add this method for easy testing in Inspector
    [ContextMenu("FORCE CLEAR ALL DATA")]
    public void ForceClearAllData()
    {
        ClearClassData();
    }

    // Public method to clear invalid data manually
    public void ClearInvalidData()
    {
        ClearClassData();
    }

    public void ShowJoinedClassesInUI()
    {
        string[] joinedClasses = GetJoinedClasses();
        if (joinedClasses.Length > 0)
        {
            string classDisplay = "Your Classes:\n";
            foreach (string className in joinedClasses)
                if (!string.IsNullOrWhiteSpace(className))
                    classDisplay += $"• {className}\n";
            if (currentClassesText != null)
            {
                currentClassesText.text = classDisplay;
                currentClassesText.gameObject.SetActive(true);
            }
        }
    }
}