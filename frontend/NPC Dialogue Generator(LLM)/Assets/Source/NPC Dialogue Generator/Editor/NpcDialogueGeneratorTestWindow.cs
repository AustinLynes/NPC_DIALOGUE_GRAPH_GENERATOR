using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public class NpcDialogueGeneratorTestWindow : EditorWindow
{
    // Client
    static NpdApiClient client;

    // Polling/status
    private enum ApiStatus { Unknown, Connected, Error, Fatal, Disconnected }
    private ApiStatus _apiStatus = ApiStatus.Unknown;
    private double lastPingTime = -1;
    private float pingIntervalSec = 10f;

    // Datasets state
    private List<Dataset> _datasets = new List<Dataset>();
    private Vector2 _leftScroll;
    private string _newDatasetId = "";
    private string _newDatasetDesc = "";
    private string _selectedDatasetId;

    // Right panel scrolls
    private Vector2 _tableScroll;

    // Dataset Samples (source + filtered for table)
    private List<DatasetSample> _samples = new List<DatasetSample>();
    private List<DatasetSample> _filteredSamples = new List<DatasetSample>();

    // Creation/Editing
    private DatasetSample _newSample = new DatasetSample();
    private int _editingSampleId = -1;
    private DatasetSampleUpdate _editingSample = new DatasetSampleUpdate();

    // Table controls
    private string _searchQuery = "";
    private enum SortField { Persona, Emotion, Text }
    private SortField _sortField = SortField.Persona;
    private bool _sortAsc = true;
    private bool _showCreatePanel = false;

    // Model configuration (UI-first; backend wiring next)
    private class SimpleModel { public int id; public string model_id; public string type; public string description; public string configJson; }
    private List<SimpleModel> _models = new List<SimpleModel>();
    private int _selectedModelIndex = -1;
    private string _modelIdInput = "";
    private string _modelDescriptionInput = "";
    private int _modelTypeIndex = 0; // 0 = base, 1 = ensemble
    private string _modelConfigJson = "{}";
    private string _modelVersionTag = "";
    private Vector2 _modelConfigScroll;
    private bool _showRawConfig = false; // false = Expanded (fields), true = Raw (JSON)

    // Training state
    private string _modelTag = "baseline_stub_v0";
    private int _epochs = 5;
    private int _batchSize = 16;
    private double _lr = 1e-4;
    private int _seed = 42;
    private string _currentTrainTaskId;
    private TaskResponse _currentTask;
    private double lastTaskPoll = -1;
    private float taskPollIntervalSec = 0.5f;

    // Busy/CTS
    private bool _isBusy;
    private CancellationTokenSource _cts;
    Hyperparamaters _hp;

    [MenuItem("NPD/Open Test Window")]
    public static void Open()
    {
        var w = GetWindow<NpcDialogueGeneratorTestWindow>("NPD");
        w.Show();
        ConnectClient();
    }

    static void ConnectClient()
    {
        if (client != null) return;
        client = new NpdApiClient("http://127.0.0.1:8000/"); // ensure this has scheme and trailing slash OK
    }

    private void OnEnable()
    {
        _cts = new CancellationTokenSource();

        ConnectClient();
        _ = RefreshDatasetsAsync();
        _ = PollApiAsync();

        UpdateFilteredSamples(); // initialize empty filtered list

        // Seed some models locally (UI-first feel; wire backend next)
        //if (_models.Count == 0)
        //{
        //    _models.Add(new SimpleModel { id = 1, model_id = "baseline_stub_v0", type = "base", description = "Stub baseline", configJson = "{}" });
        //    _models.Add(new SimpleModel { id = 2, model_id = "style_ensemble_v1", type = "ensemble", description = "Two-component style blend", configJson = "{\"components\":[{\"model_id\":\"a\",\"weight\":0.6},{\"model_id\":\"b\",\"weight\":0.4}]}" });
        //    _selectedModelIndex = 0;
        //    LoadModelIntoInputs(_models[_selectedModelIndex]);
        //    SyncModelSelectionToTrainingTag();
        //}
        _models.Clear();
    }

    private void OnDisable()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public async void OnGUI()
    {
        DrawApiHeader();

        // Layout rects
        float headerH = 50f;
        float leftW = 280f;
        Rect leftRect = new Rect(0, headerH + 1, leftW, position.height - headerH - 1);
        Rect rightRect = new Rect(leftW + 1, headerH + 1, position.width - leftW - 1, position.height - headerH - 1);

        // Backgrounds
        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(leftRect, new Color(0.12f, 0.12f, 0.12f));
            EditorGUI.DrawRect(rightRect, new Color(0.10f, 0.10f, 0.10f));
            // Divider
            EditorGUI.DrawRect(new Rect(leftW, headerH, 1, position.height - headerH), new Color(0, 0, 0, 0.6f));
        }

        DrawLeftPanel(leftRect);
        DrawRightPanel(rightRect);

        // Timed polling: API status
        double now = EditorApplication.timeSinceStartup;
        if (now - lastPingTime > pingIntervalSec)
        {
            lastPingTime = now;
            await PollApiAsync();
        }

        // Timed polling: Task status
        if (!string.IsNullOrEmpty(_currentTrainTaskId) && now - lastTaskPoll > taskPollIntervalSec)
        {
            lastTaskPoll = now;
            _ = PollTaskAsync(_currentTrainTaskId);
        }
    }

    private void DrawApiHeader()
    {
        float headerHeight = 50f;
        Rect fullHeaderRect = new Rect(0, 0, position.width, headerHeight);

        if (Event.current.type == EventType.Repaint)
            EditorGUI.DrawRect(fullHeaderRect, new Color(0.13f, 0.13f, 0.13f));

        // Title
        var title = "NPD API";
        GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 18 };
        Vector2 titleSize = titleStyle.CalcSize(new GUIContent(title));
        Rect titleRect = new Rect(16, 16, titleSize.x, titleSize.y);
        GUI.Label(titleRect, title, titleStyle);

        // Status panel
        float panelW = 240f, panelH = 24f;
        Rect statusRect = new Rect(fullHeaderRect.xMax - (panelW / 2) - 16, 12, panelW, panelH);
        DrawStatusPanel(statusRect, _apiStatus);
    }

    private void DrawStatusPanel(Rect container, ApiStatus status)
    {
        Color color = status switch
        {
            ApiStatus.Connected => Color.green,
            ApiStatus.Error => Color.yellow,
            ApiStatus.Fatal => Color.red,
            ApiStatus.Disconnected => new Color(0.5f, 0.5f, 0.5f),
            _ => Color.gray
        };
        string label = status switch
        {
            ApiStatus.Connected => "Connection OK",
            ApiStatus.Error => "Connection OK + Error",
            ApiStatus.Fatal => "Connection OK + Fatal",
            ApiStatus.Disconnected => "No Connection",
            _ => "Unknown"
        };

        if (Event.current.type == EventType.Repaint)
            EditorGUI.DrawRect(container, new Color(0.06f, 0.06f, 0.06f));

        float circle = 12f;
        Rect circleRect = new Rect(container.x + 8, container.y + (container.height - circle) * 0.5f, circle, circle);
        Texture2D circleTex = MakeCircleTexture(color, (int)circle);
        GUI.DrawTexture(circleRect, circleTex, ScaleMode.ScaleToFit, true, 1f);

        GUIStyle labelStyle = new GUIStyle(EditorStyles.label);
        labelStyle.normal.textColor = Color.white;
        labelStyle.hover.textColor = Color.white;
        labelStyle.active.textColor = Color.white;
        labelStyle.focused.textColor = Color.white;

        Rect labelRect = new Rect(circleRect.xMax + 8, container.y, container.width - circle - 24, container.height);
        GUI.Label(labelRect, label, labelStyle);
    }

    private Texture2D MakeCircleTexture(Color color, int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.ARGB32, false) { filterMode = FilterMode.Bilinear };
        Color transparent = new Color(0, 0, 0, 0);
        float r = size / 2f - 1;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - size / 2f, dy = y - size / 2f;
                tex.SetPixel(x, y, (dx * dx + dy * dy) <= r * r ? color : transparent);
            }
        tex.Apply();
        return tex;
    }

    private void DrawLeftPanel(Rect rect)
    {
        // Title
        GUIStyle h = new GUIStyle(EditorStyles.boldLabel);
        Rect hRect = new Rect(rect.x + 12, rect.y + 8, rect.width - 24, 20);
        GUI.Label(hRect, "Datasets", h);

        // Create form
        float y = hRect.yMax + 8;
        float lineH = 18;

        EditorGUI.LabelField(new Rect(rect.x + 12, y, 90, lineH), "Dataset ID:");
        _newDatasetId = EditorGUI.TextField(new Rect(rect.x + 110, y, rect.width - 122, lineH), _newDatasetId);
        y += lineH + 4;

        EditorGUI.LabelField(new Rect(rect.x + 12, y, 90, lineH), "Description:");
        _newDatasetDesc = EditorGUI.TextField(new Rect(rect.x + 110, y, rect.width - 122, lineH), _newDatasetDesc);
        y += lineH + 6;

        if (GUI.Button(new Rect(rect.x + 12, y, rect.width - 24, 22), "Create Dataset"))
        {
            _ = CreateDatasetAsync(_newDatasetId, _newDatasetDesc);
        }
        y += 22 + 10;

        // Divider
        EditorGUI.DrawRect(new Rect(rect.x + 12, y, rect.width - 24, 1), new Color(0, 0, 0, 0.6f));
        y += 6;

        Color baseColor = new Color(0.34f, 0.34f, 0.34f); // neutral label color
        Color hoverColor = new Color(0.27f, 0.51f, 0.71f); // DodgerBlue

        GUIStyle refreshStyle = new GUIStyle(EditorStyles.label);
        refreshStyle.alignment = TextAnchor.MiddleCenter;
        refreshStyle.fontSize = 16;
        refreshStyle.padding = new RectOffset(0, 0, 0, 0);
        refreshStyle.border = new RectOffset(0, 0, 0, 0);
        refreshStyle.margin = new RectOffset(0, 0, 0, 0);
        

        // Detect hover
        Rect refreshRect = new Rect(rect.x + (rect.width) - 20, y, 20, lineH);
        bool isHovered = refreshRect.Contains(Event.current.mousePosition);

        Color textColor = isHovered ? hoverColor : baseColor;
        refreshStyle.normal.textColor = textColor;

        // Draw as label with button clickable area
        GUI.Label(new Rect(rect.x + 12, y, rect.width - 24, lineH), "All Datasets", EditorStyles.miniBoldLabel);
        EditorGUI.LabelField(refreshRect, "\u21BB", refreshStyle);
        if (GUI.Button(refreshRect, GUIContent.none, GUIStyle.none))
        {
            _ = RefreshDatasetsAsync();
        }
        y += lineH;

        // Scroll list
        Rect viewRect = new Rect(rect.x + 12, y, rect.width - 24, rect.yMax - y - 12);
        Rect innerRect = new Rect(0, 0, viewRect.width - 16, Mathf.Max(viewRect.height, _datasets.Count * 24 + 4));
        _leftScroll = GUI.BeginScrollView(viewRect, _leftScroll, innerRect);

        float iy = 2;
        foreach (var ds in _datasets)
        {
            Rect row = new Rect(0, iy, innerRect.width, 22);
            bool selected = ds.dataset_id == _selectedDatasetId;
            if (selected && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(row, new Color(0.18f, 0.18f, 0.18f));

            if (GUI.Button(row, GUIContent.none, GUIStyle.none))
            {
                _selectedDatasetId = ds.dataset_id;
                _ = LoadSamplesAsync(_selectedDatasetId);
            }

            GUI.Label(new Rect(row.x + 8, row.y + 3, row.width - 16, row.height - 6), $"{ds.dataset_id}  —  {ds.description}");
            iy += 24;
        }

        GUI.EndScrollView();
    }

    private void DrawRightPanel(Rect rect)
    {
        // Header
        GUIStyle h = new GUIStyle(EditorStyles.boldLabel);
        Rect hRect = new Rect(rect.x + 12, rect.y + 8, rect.width - 24, 20);
        GUI.Label(hRect, string.IsNullOrEmpty(_selectedDatasetId) ? "Select a dataset" : $"Dataset: {_selectedDatasetId}", h);

        if (string.IsNullOrEmpty(_selectedDatasetId))
            return;

        float y = hRect.yMax + 6;

        // Model configuration panel (above samples table)
        // Only show config if a model is selected
        bool hasSelectedModel = _selectedModelIndex >= 0 && _selectedModelIndex < _models.Count;
        if (hasSelectedModel)
        {
            float modelPanelH = 200f;
            Rect modelRect = new Rect(rect.x + 12, y, rect.width - 24, modelPanelH);
            DrawModelConfigPanel(modelRect);
            y += modelPanelH + 8;
        }
        else
        {
            // Optional: subtle hint
            GUI.Label(new Rect(rect.x + 16, y, rect.width - 32, 18), "Select or create a model to edit its configuration.", EditorStyles.miniLabel);
            y += 24;
        }


        // Toolbar
        float toolbarH = 26f;
        Rect toolbarRect = new Rect(rect.x + 12, y, rect.width - 24, toolbarH);
        DrawSamplesToolbar(toolbarRect);
        y += toolbarH + 6;

        // Inline creation panel (optional)
        if (_showCreatePanel)
        {
            float createH = 108f;
            Rect createRect = new Rect(rect.x + 12, y, rect.width - 24, createH);
            DrawSampleCreatePanel(createRect);
            y += createH + 8;
        }

        // Table header + rows region
        float bottomReserved = 112f; // space reserved for training box (approx 92 + margins)
        float tableH = Mathf.Max(0, rect.height - (y - rect.y) - bottomReserved);
        Rect tableRect = new Rect(rect.x + 12, y, rect.width - 24, tableH);
        DrawSamplesTable(tableRect);

        Rect barRect = new Rect(rect.x + 12, rect.yMax - 48, rect.width - 24, 40);
        EditorGUI.DrawRect(barRect, new Color(0.08f, 0.08f, 0.08f));

        float bx = barRect.x + 8, by = barRect.y + 10, bh = 20;

        // Model selector
        GUI.Label(new Rect(bx, by, 50, bh), "Model:");
        string[] modelOptions = _models.Select(m => m.model_id).ToArray();
        int newSelection = _models.Count > 0 ? EditorGUI.Popup(new Rect(bx + 52, by, 180, bh), Mathf.Clamp(_selectedModelIndex, 0, _models.Count - 1), modelOptions) : -1;
        if (_models.Count > 0 && newSelection != _selectedModelIndex)
        {
            _selectedModelIndex = newSelection;
            LoadModelIntoInputs(_models[_selectedModelIndex]);
            SyncModelSelectionToTrainingTag();
        }

        // Create shell model (ID only)
        float createX = (bx + 52 + 180) + 10;
        GUI.Label(new Rect(createX, by, 70, bh), "New ID:");
        _modelIdInput = EditorGUI.TextField(new Rect(createX + 64, by, 140, bh), _modelIdInput);

        Rect createBtn = new Rect(createX + 64 + 146, by, 80, bh);
        if (GUI.Button(createBtn, "Create"))
        {
            if (string.IsNullOrWhiteSpace(_modelIdInput))
            {
                Debug.LogWarning("Model ID required");
            }
            else if (_models.Any(m => m.model_id == _modelIdInput))
            {
                Debug.LogWarning("Model ID already exists");
            }
            else
            {
                var newId = (_models.Count == 0 ? 1 : _models.Max(mm => mm.id) + 1);
                var shell = new SimpleModel
                {
                    id = newId,
                    model_id = _modelIdInput.Trim(),
                    description = "",
                    type = "base",
                    configJson = "{}"
                };
                _models.Add(shell);
                _selectedModelIndex = _models.Count - 1;
                LoadModelIntoInputs(shell);
                SyncModelSelectionToTrainingTag();
                // TODO: POST /models { model_id } as a shell to persist server-side
            }
        }

        // Start/Pause/Stop + progress
        float controlsX = createBtn.xMax + 12;
        
        Rect startBtn = new Rect(controlsX, by, 200, bh);
        //Rect pauseBtn = new Rect(startBtn.xMax + 6, by, 70, bh);
        //Rect stopBtn = new Rect(pauseBtn.xMax + 6, by, 70, bh);
        Rect progressBar = new Rect(startBtn.xMax + 12, by, 500, bh);

        EditorGUI.BeginDisabledGroup(_selectedModelIndex < 0 || string.IsNullOrEmpty(_selectedDatasetId));
        
        if (GUI.Button(startBtn, "Train With Dataset"))
            _ = StartTrainingAsync();

        EditorGUI.EndDisabledGroup();

        //if (GUI.Button(pauseBtn, "Pause"))
        //{
        //    // TODO: implement pause logic
        //}
        //if (GUI.Button(stopBtn, "Stop"))
        //{
        //    // TODO: implement stop logic
        //}

        if (_currentTask != null)
            EditorGUI.ProgressBar(progressBar, Mathf.Clamp01(_currentTask.progress), $"{_currentTask.state} — {_currentTask.message}");

    }

    private void DrawModelConfigPanel(Rect r)
    {
        if (!(_selectedModelIndex >= 0 && _selectedModelIndex < _models.Count))
            return;

        if (Event.current.type == EventType.Repaint)
            EditorGUI.DrawRect(r, new Color(0.085f, 0.085f, 0.085f));

        Hyperparamaters parsedHp;
        try
        {
            parsedHp = JsonConvert.DeserializeObject<Hyperparamaters>(_modelConfigJson) ?? new Hyperparamaters();
        }
        catch
        {
            parsedHp = new Hyperparamaters(); // fallback
        }


        float x = r.x + 8, y = r.y + 8, lh = 18f, pad = 6f;
        float lw = 90f;
        float rightW = r.width - lw - 16;

        // Row 1: Select existing model dropdown + Refresh
        EditorGUI.LabelField(new Rect(x, y, lw, lh), "Model:");
        string[] modelOptions = _models.Count > 0 ? _models.Select(m => $"{m.model_id} ({m.type})").ToArray() : new[] { "(none)" };
        int safeIndex = Mathf.Clamp(_selectedModelIndex, _models.Count > 0 ? 0 : -1, Math.Max(_models.Count - 1, -1));
        int newIndex = EditorGUI.Popup(new Rect(x + lw, y, rightW - 100, lh), safeIndex, modelOptions);
        if (_models.Count > 0 && newIndex != _selectedModelIndex)
        {
            _selectedModelIndex = newIndex;
            LoadModelIntoInputs(_models[_selectedModelIndex]);
            SyncModelSelectionToTrainingTag();
        }

        if (GUI.Button(new Rect(r.xMax - 88, y, 80, lh), "Refresh"))
        {
            // TODO: wire to backend ListModels endpoint; UI-first now
            // _ = RefreshModelsAsync();
        }
        y += lh + pad;

        // Row 2: Model ID + Type
        EditorGUI.LabelField(new Rect(x, y, lw, lh), "Model ID:");
        _modelIdInput = EditorGUI.TextField(new Rect(x + lw, y, rightW - 120, lh), _modelIdInput);

        EditorGUI.LabelField(new Rect(r.xMax - 120, y, 50, lh), "Type:");
        _modelTypeIndex = EditorGUI.Popup(new Rect(r.xMax - 70, y, 60, lh), _modelTypeIndex, new[] { "base", "ensemble" });
        y += lh + pad;

        // Row 3: Description
        EditorGUI.LabelField(new Rect(x, y, lw, lh), "Description:");
        _modelDescriptionInput = EditorGUI.TextField(new Rect(x + lw, y, rightW, lh), _modelDescriptionInput);
        y += lh + pad;

        // Config display mode toggle
        EditorGUI.LabelField(new Rect(x, y, lw, lh), "Config:");
        _showRawConfig = EditorGUI.ToggleLeft(new Rect(x + lw, y, 100, lh), "Show Raw", _showRawConfig);
        bool toggledRaw = EditorGUI.ToggleLeft(new Rect(x + lw, y, 100, lh), "Show Raw", _showRawConfig);
        if (toggledRaw != _showRawConfig)
        {
            _showRawConfig = toggledRaw;

            if (!_showRawConfig)
            {
                // Going from Raw -> Expanded: re-hydrate parsed fields
                try
                {
                    var parsed = JsonConvert.DeserializeObject<Hyperparamaters>(_modelConfigJson);
                    if (parsed != null)
                        _hp = parsed;
                }
                catch
                {
                    Debug.LogWarning("Failed to parse config for expanded view");
                }
            }
        }
        // Config editor
        if (_showRawConfig)
        {
            // Raw JSON editor
            Rect cfgRect = new Rect(x + lw, y + lh + 2, rightW, 80);
            EditorGUI.DrawRect(cfgRect, new Color(0.07f, 0.07f, 0.07f));
            Rect cfgScrollView = new Rect(cfgRect.x, cfgRect.y, cfgRect.width, cfgRect.height);
            Rect cfgContent = new Rect(0, 0, cfgRect.width - 16, 80 - 12);
            _modelConfigScroll = GUI.BeginScrollView(cfgScrollView, _modelConfigScroll, cfgContent);
            _modelConfigJson = EditorGUI.TextArea(new Rect(4, 4, cfgContent.width - 8, cfgContent.height - 8), _modelConfigJson);
            GUI.EndScrollView();
            y += 80 + lh + pad + 2;
        }
        else
        {
            if (_selectedModelIndex >= 0 && _selectedModelIndex < _models.Count && !_showRawConfig)
                _modelConfigJson = JsonConvert.SerializeObject(parsedHp, Formatting.Indented);

            // Expanded hyperparameters editor
            y += lh + pad;

            EditorGUI.LabelField(new Rect(x, y, lw, lh), "Epochs:");
            parsedHp.epochs = EditorGUI.IntField(new Rect(x + lw, y, 60, lh), parsedHp.epochs);

            EditorGUI.LabelField(new Rect(x + lw + 70, y, lw, lh), "Batch:");
            parsedHp.batch_size = EditorGUI.IntField(new Rect(x + lw + 70 + lw, y, 60, lh), parsedHp.batch_size);

            EditorGUI.LabelField(new Rect(x + lw + 2 * (70 + lw), y, lw, lh), "LR:");
            parsedHp.lr = EditorGUI.DoubleField(new Rect(x + lw + 2 * (70 + lw) + lw, y, 80, lh), parsedHp.lr);

            EditorGUI.LabelField(new Rect(x + lw + 3 * (70 + lw) + 80, y, lw, lh), "Seed:");
            parsedHp.seed = EditorGUI.IntField(new Rect(x + lw + 3 * (70 + lw) + 80 + lw, y, 60, lh), parsedHp.seed);

            y += lh + pad;
        }


        // Row 5: Footer buttons (Save/Update, Quick Train, Version Tag)
        Rect saveBtn = new Rect((r.x + r.width) - 120, (r.y + r.height) - 20, 120, 22);
        Rect versionLabel = new Rect(x, y, 80, 18);
        Rect versionField = new Rect(versionLabel.xMax + 4, y + 2, 140, 18);

        if (GUI.Button(saveBtn, _selectedModelIndex >= 0 ? "Save" : "Create Model"))
        {
            if (string.IsNullOrWhiteSpace(_modelIdInput))
            {
                Debug.LogWarning("Model ID is required");
            }
            else
            {
                if (!_showRawConfig)
                    _modelConfigJson = JsonConvert.SerializeObject(parsedHp, Formatting.Indented);

                var existing = _models.FindIndex(m => m.model_id == _modelIdInput);
                if (existing >= 0)
                {
                    _models[existing].description = _modelDescriptionInput;
                    _models[existing].type = _modelTypeIndex == 0 ? "base" : "ensemble";
                    _models[existing].configJson = _modelConfigJson;
                    _selectedModelIndex = existing;
                }
                else
                {
                    var nextId = (_models.Count == 0 ? 1 : _models.Max(m => m.id) + 1);
                    _models.Add(new SimpleModel
                    {
                        id = nextId,
                        model_id = _modelIdInput,
                        description = _modelDescriptionInput,
                        type = _modelTypeIndex == 0 ? "base" : "ensemble",
                        configJson = _modelConfigJson
                    });
                    _selectedModelIndex = _models.Count - 1;
                }
                SyncModelSelectionToTrainingTag();
                // TODO: backend Create/Update call
            }
        }

        GUI.Label(versionLabel, "Version Tag:");
        _modelVersionTag = EditorGUI.TextField(versionField, _modelVersionTag);
    }

    private void LoadModelIntoInputs(SimpleModel m)
    {
        if (m == null) return;
        _modelIdInput = m.model_id;
        _modelDescriptionInput = m.description ?? "";
        _modelTypeIndex = m.type == "ensemble" ? 1 : 0;
        _modelConfigJson = string.IsNullOrWhiteSpace(m.configJson) ? "{}" : m.configJson;
    }

    private void SyncModelSelectionToTrainingTag()
    {
        if (_selectedModelIndex >= 0 && _selectedModelIndex < _models.Count)
        {
            _modelTag = _models[_selectedModelIndex].model_id;
        }
        else if (!string.IsNullOrWhiteSpace(_modelIdInput))
        {
            _modelTag = _modelIdInput;
        }
    }

    private void DrawSamplesToolbar(Rect r)
    {
        if (Event.current.type == EventType.Repaint)
            EditorGUI.DrawRect(r, new Color(0.08f, 0.08f, 0.08f));

        float pad = 6f;
        float x = r.x + pad, y = r.y + 4f;

        // Sort label + dropdown
        GUI.Label(new Rect(x, y, 36, 18), "Sort:");
        x += 36 + 4;

        string[] sortOptions = { "Persona", "Emotion", "Text" };
        int newSort = EditorGUI.Popup(new Rect(x, y, 110, 18), (int)_sortField, sortOptions);
        if (newSort != (int)_sortField)
        {
            _sortField = (SortField)newSort;
            UpdateFilteredSamples();
        }
        x += 110 + 8;

        // Asc toggle
        bool newAsc = EditorGUI.ToggleLeft(new Rect(x, y, 60, 18), "Asc", _sortAsc);
        if (newAsc != _sortAsc)
        {
            _sortAsc = newAsc;
            UpdateFilteredSamples();
        }

        // Right edge controls
        float addBtnW = 90f;
        float cancelBtnW = 90f;
        float searchW = 200f;
        float right = r.xMax - pad;

        // Add/Cancel button
        float actW = _showCreatePanel ? cancelBtnW : addBtnW;
        Rect actionRect = new Rect(right - actW, y, actW, 18);
        if (!_showCreatePanel)
        {
            if (GUI.Button(actionRect, "+ Add Sample"))
                _showCreatePanel = true;
        }
        else
        {
            if (GUI.Button(actionRect, "Cancel"))
                _showCreatePanel = false;
        }
        right -= actW + 8;

        // Search label + field
        float searchLabelW = 54f;
        Rect searchLabelRect = new Rect(right - (searchLabelW + searchW + 6), y, searchLabelW, 18);
        GUI.Label(searchLabelRect, "Search:");
        Rect searchRect = new Rect(searchLabelRect.xMax + 2, y, searchW, 18);
        string newQuery = EditorGUI.TextField(searchRect, _searchQuery);
        if (newQuery != _searchQuery)
        {
            _searchQuery = newQuery;
            UpdateFilteredSamples();
        }
    }

    private void DrawSamplesTable(Rect r)
    {
        // Header
        Rect header = new Rect(r.x, r.y, r.width, 22);
        if (Event.current.type == EventType.Repaint)
            EditorGUI.DrawRect(header, new Color(0.11f, 0.11f, 0.11f));

        // Column layout
        float pad = 8f;
        float personaW = 140f;
        float emotionW = 120f;
        float actionsW = 60f;
        float textW = r.width - (personaW + emotionW + actionsW + pad * 5);

        Rect cPersona = new Rect(header.x + pad, header.y + 2, personaW, 18);
        Rect cEmotion = new Rect(cPersona.xMax + pad, header.y + 2, emotionW, 18);
        Rect cText = new Rect(cEmotion.xMax + pad, header.y + 2, textW, 18);
        Rect cActions = new Rect(cText.xMax + pad, header.y + 2, actionsW, 18);

        GUI.Label(cPersona, "Persona", EditorStyles.miniBoldLabel);
        GUI.Label(cEmotion, "Emotion", EditorStyles.miniBoldLabel);
        GUI.Label(cText, "Text", EditorStyles.miniBoldLabel);
        GUI.Label(cActions, "Actions", EditorStyles.miniBoldLabel);

        // Rows area
        Rect rowsRect = new Rect(r.x, header.yMax + 1, r.width, r.height - header.height - 1);
        Rect inner = new Rect(0, 0, rowsRect.width - 16, Mathf.Max(rowsRect.height, _filteredSamples.Count * 28f + 2));
        _tableScroll = GUI.BeginScrollView(rowsRect, _tableScroll, inner);

        float y = 2f;
        for (int i = 0; i < _filteredSamples.Count; i++)
        {
            var s = _filteredSamples[i];
            Rect row = new Rect(0, y, inner.width, 26f);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(row, (i % 2 == 0) ? new Color(0.13f, 0.13f, 0.13f) : new Color(0.115f, 0.115f, 0.115f));

            Rect rPersona = new Rect(row.x + pad, row.y + 4, personaW, 18);
            Rect rEmotion = new Rect(rPersona.xMax + pad, row.y + 4, emotionW, 18);
            Rect rText = new Rect(rEmotion.xMax + pad, row.y + 4, textW, 18);
            Rect rEdit = new Rect(rText.xMax + pad, row.y + 4, 26, 18);
            Rect rDel = new Rect(rEdit.xMax + 4, row.y + 4, 26, 18);

            GUI.Label(rPersona, s.persona, EditorStyles.miniLabel);
            GUI.Label(rEmotion, s.emotion, EditorStyles.miniLabel);
            GUI.Label(rText, Truncate(s.text, Mathf.Max(10, (int)(textW / 7.0f))), EditorStyles.miniLabel);

            if (GUI.Button(rEdit, "✎"))
            {
                _editingSampleId = s.id;
                _editingSample = new DatasetSampleUpdate
                {
                    persona = s.persona,
                    emotion = s.emotion,
                    text = s.text,
                    tags = s.tags
                };
                // Optional: inline editor in a future iteration
            }
            if (GUI.Button(rDel, "✖"))
            {
                _ = DeleteSampleAsync(_selectedDatasetId, s.id);
            }

            y += 28f;
        }

        GUI.EndScrollView();
    }

    private void DrawSampleCreatePanel(Rect r)
    {
        if (Event.current.type == EventType.Repaint)
            EditorGUI.DrawRect(r, new Color(0.08f, 0.08f, 0.08f));

        float x = r.x + 8, y = r.y + 8, lw = 80f;
        float rw = (r.width - lw - 24);

        EditorGUI.LabelField(new Rect(x, y, lw, 18), "Persona:");
        _newSample.persona = EditorGUI.TextField(new Rect(x + lw, y, rw, 18), _newSample.persona);
        y += 20;

        EditorGUI.LabelField(new Rect(x, y, lw, 18), "Emotion:");
        _newSample.emotion = EditorGUI.TextField(new Rect(x + lw, y, rw, 18), _newSample.emotion);
        y += 20;

        EditorGUI.LabelField(new Rect(x, y, lw, 18), "Text:");
        _newSample.text = EditorGUI.TextField(new Rect(x + lw, y, rw, 18), _newSample.text);
        y += 28;

        Rect addBtn = new Rect(x, y, 120, 22);
        Rect cancelBtn = new Rect(addBtn.xMax + 8, y, 120, 22);

        if (GUI.Button(addBtn, "Add Sample"))
        {
            var payload = new DatasetSample
            {
                id = 0,
                persona = _newSample.persona,
                emotion = _newSample.emotion,
                text = _newSample.text,
                tags = _newSample.tags ?? new List<string>()
            };
            _ = CreateSampleAsync(_selectedDatasetId, payload);
            _showCreatePanel = false;
        }
        if (GUI.Button(cancelBtn, "Cancel"))
        {
            _showCreatePanel = false;
        }
    }

    private void UpdateFilteredSamples()
    {
        IEnumerable<DatasetSample> q = _samples;

        if (!string.IsNullOrWhiteSpace(_searchQuery))
        {
            string needle = _searchQuery.Trim();
            q = q.Where(s =>
                (!string.IsNullOrEmpty(s.persona) && s.persona.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (!string.IsNullOrEmpty(s.emotion) && s.emotion.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (!string.IsNullOrEmpty(s.text) && s.text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
            );
        }

        IOrderedEnumerable<DatasetSample> ordered = _sortField switch
        {
            SortField.Persona => q.OrderBy(s => s.persona ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            SortField.Emotion => q.OrderBy(s => s.emotion ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            _ => q.OrderBy(s => s.text ?? string.Empty, StringComparer.OrdinalIgnoreCase),
        };

        ordered = ordered.ThenBy(s => s.id);

        var list = ordered.ToList();
        if (!_sortAsc) list.Reverse();

        _filteredSamples = list;
        Repaint();
    }

    private string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
    }

    // --------- Async ops ---------

    private async Task RefreshDatasetsAsync()
    {
        if (_apiStatus == ApiStatus.Unknown || _apiStatus == ApiStatus.Disconnected) return;

        try
        {
            var list = await client.ListDatasetsAsync(_cts.Token);
            _datasets = list ?? new List<Dataset>();
            if (!string.IsNullOrEmpty(_selectedDatasetId) && _datasets.Find(d => d.dataset_id == _selectedDatasetId) == null)
                _selectedDatasetId = null;
        }
        catch (Exception ex)
        {
            Debug.LogError($"List datasets failed: {ex.Message}");
        }
        Repaint();
    }

    private async Task CreateSampleAsync(string datasetId, DatasetSample sample)
    {
        try
        {
            await client.CreateSampleAsync(datasetId, sample, _cts.Token);
            _newSample = new DatasetSample(); // clear after send
            await LoadSamplesAsync(datasetId);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Create sample failed: {ex.Message}");
        }
        Repaint();
    }

    private async Task DeleteSampleAsync(string datasetId, int sampleId)
    {
        try
        {
            await client.DeleteSampleAsync(datasetId, sampleId, _cts.Token);
            await LoadSamplesAsync(datasetId);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Delete sample failed: {ex.Message}");
        }
        Repaint();
    }

    private async Task UpdateSampleAsync(string datasetId, int sampleId, DatasetSampleUpdate update)
    {
        try
        {
            await client.UpdateSampleAsync(datasetId, sampleId, update, _cts.Token);
            _editingSampleId = -1;
            await LoadSamplesAsync(datasetId);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Update sample failed: {ex.Message}");
        }
        Repaint();
    }

    private async Task CreateDatasetAsync(string datasetId, string desc)
    {
        if (string.IsNullOrWhiteSpace(datasetId)) return;
        try
        {
            var payload = new CreateDatasetRequest
            {
                dataset_id = datasetId,
                description = string.IsNullOrWhiteSpace(desc) ? null : desc,
                samples = null
            };
            var created = await client.CreateDatasetAsync(payload, _cts.Token);
            _newDatasetId = "";
            _newDatasetDesc = "";
            await RefreshDatasetsAsync();
            _selectedDatasetId = created.dataset_id;
            _ = LoadSamplesAsync(_selectedDatasetId);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Create dataset failed: {ex.Message}");
        }
        Repaint();
    }

    private async Task LoadSamplesAsync(string datasetId)
    {
        try
        {
            var list = await client.GetDatasetSamples(datasetId, _cts.Token);
            _samples = list ?? new List<DatasetSample>();
            UpdateFilteredSamples();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Get samples failed: {ex.Message}");
            _samples = new List<DatasetSample>();
            UpdateFilteredSamples();
        }
        Repaint();
    }

    private async Task StartTrainingAsync()
    {
        if (string.IsNullOrEmpty(_selectedDatasetId)) return;
        try
        {
            var req = new TrainRequest
            {
                model_tag = _modelTag,
                dataset_id = _selectedDatasetId,
                hyperparamaters = new Hyperparamaters
                {
                    epochs = _epochs,
                    batch_size = _batchSize,
                    lr = _lr,
                    seed = _seed
                }
            };
            var task = await client.StartTrainingAsync(req, _cts.Token);
            _currentTrainTaskId = task.task_id;
            _currentTask = task;
            lastTaskPoll = 0; // trigger immediate poll
        }
        catch (Exception ex)
        {
            Debug.LogError($"Start training failed: {ex.Message}");
        }
        Repaint();
    }

    private async Task PollTaskAsync(string taskId)
    {
        try
        {
            var t = await client.GetTrainingTaskAsync(taskId, _cts.Token);
            _currentTask = t;
            if (t.state == "succeeded" || t.state == "failed")
            {
                // Keep final state; polling cadence will effectively stop if _currentTrainTaskId is left as-is
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Poll task failed: {ex.Message}");
        }
        Repaint();
    }

    private async Task PollApiAsync()
    {
        try
        {
            ConnectClient(); // sets up base URL safely
            var res = await client.PingAsync(); // GET /ping
            var json = JsonConvert.SerializeObject(res);
            if (res.TryGetValue("ok", out object value))
            {
                bool connected = (bool)value;
                _apiStatus = connected ? ApiStatus.Connected : ApiStatus.Error;
            }
        }
        catch (Exception ex)
        {
            _apiStatus = ApiStatus.Disconnected;
            EditorGUILayout.HelpBox($"{ex.Message}", MessageType.Error);
        }
        Repaint();
    }
}
