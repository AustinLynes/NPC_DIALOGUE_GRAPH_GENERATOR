using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

public class NpdApiClient 
{
    private readonly string _baseURL;
    private readonly HttpClient _http;

    public NpdApiClient(string baseURL, HttpClient httpClient=null)
    {
        _baseURL = baseURL.TrimEnd('/');
        _http = httpClient ?? new HttpClient();

        if (string.IsNullOrWhiteSpace(baseURL))
            throw new ArgumentException("Base URL cannot be null or whitespace");

        if (!Uri.TryCreate(baseURL, UriKind.Absolute, out var baseAddress))
            throw new UriFormatException($"Invalid URI: '{baseURL}'");

        _http.BaseAddress = baseAddress; // e.g. "http://127.0.0.1:8000"
       
        _http.Timeout = TimeSpan.FromSeconds(20);
    }

    async Task<T> GetAsync<T>(string path, CancellationToken ct=default)
    {
        string body = null;
        using(var res = await _http.GetAsync(path, ct))
        {
            body = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
            {
                //throw new Exception($"[GET] {path}-> {(int)res.StatusCode}: {body}");
                return default;
            }
        }
        return JsonConvert.DeserializeObject<T>(body);
    }

    async Task<T> PostJsonAsync<T>(string path, object payload, CancellationToken ct = default)
    {
        string json = JsonConvert.SerializeObject(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await _http.PostAsync(path, content, ct);
        string body = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
        {
            throw new Exception($"[POST] {path} -> {(int)res.StatusCode}: {body}");
        }

        return JsonConvert.DeserializeObject<T>(body);
    }

    private async Task PostJsonAsync(string path, object payload, CancellationToken ct = default)
    {
        string json = JsonConvert.SerializeObject(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync(path, content, ct);
        string body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"[POST] {path} -> {(int)resp.StatusCode}: {body}");
    }

    private async Task<T> PutJsonAsync<T>(string path, object payload, CancellationToken ct = default)
    {
        string json = JsonConvert.SerializeObject(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await _http.PutAsync(path, content, ct);
        string body = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
        {
            throw new Exception($"[POST] {path} -> {(int)res.StatusCode}: {body}");
        }

        return JsonConvert.DeserializeObject<T>(body);
    }

    private async Task<T> DeleteAsync<T>(string path, CancellationToken ct = default)
    {
        using var res = await _http.DeleteAsync(path, ct);
        string body = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
        {
            throw new Exception($"[DEL] {path} -> {(int)res.StatusCode}: {body}");
        }

        return JsonConvert.DeserializeObject<T>(body);
    }

    // ------------------------------------------------
    // DATASETS
    // ------------------------------------------------

    // CREATE a Dataset.
    public Task<Dataset> CreateDatasetAsync(CreateDatasetRequest req, CancellationToken ct = default) => PostJsonAsync<Dataset>("/datasets", req, ct);
    // GET a dataset.
    public Task<Dataset> GetDatasetAsync(string dataset_id,  CancellationToken ct = default) => GetAsync<Dataset>($"/datasets/{dataset_id}", ct);
    public Task<Dictionary<string, string>> DeleteDatasetAsync(string dataset_id, CancellationToken cancellationToken = default) 
        => DeleteDatasetAsync($"/datasets/{dataset_id}", cancellationToken);
    public Task<Dataset> UpdateDatasetAsync(string dataset_id, UpdateDatasetRequest update, CancellationToken ct = default)
        => PutJsonAsync<Dataset>($"datasets/{dataset_id}", update, ct);

    // GET all datasets.
    public Task<List<Dataset>> ListDatasetsAsync(CancellationToken ct = default) => GetAsync<List<Dataset>>("/datasets", ct);

    // get all samples from the dataset.
    public Task<List<DatasetSample>> GetDatasetSamples(string dataset_id, CancellationToken ct = default) 
        => GetAsync<List<DatasetSample>>($"/datasets/{dataset_id}/samples", ct);
    public Task<DatasetSampleOut> CreateSampleAsync(string datasetId, DatasetSample sample, CancellationToken ct = default)
    => PostJsonAsync<DatasetSampleOut>($"/datasets/{datasetId}/samples/new", sample, ct);

    public Task<DatasetSampleOut> UpdateSampleAsync(string datasetId, int sampleId, DatasetSampleUpdate update, CancellationToken ct = default)
        => PutJsonAsync<DatasetSampleOut>($"/datasets/{datasetId}/samples/{sampleId}", update, ct);

    public Task<Dictionary<string, string>> DeleteSampleAsync(string datasetId, int sampleId, CancellationToken ct = default)
        => DeleteAsync<Dictionary<string, string>>($"/datasets/{datasetId}/samples/{sampleId}", ct);




    // ------------------------------------------------
    // TRAINING / EVALUATION
    // ------------------------------------------------

    public Task<TaskResponse> StartTrainingAsync(TrainRequest req, CancellationToken ct = default)
        => PostJsonAsync<TaskResponse>("/training/start", req, ct);

    public Task<TaskResponse> GetTrainingTaskAsync(string task_id, CancellationToken ct = default) 
        => GetAsync<TaskResponse>($"training/{task_id}", ct);

    public Task<TaskResponse> StartEvaluationTaskAsync(EvaluationRequest req, CancellationToken ct = default)
        => PostJsonAsync<TaskResponse>("/evaluation/start", req, ct);

    public Task<TaskResponse> GetEvaluationTaskAsync(string task_id, CancellationToken ct = default)
        => GetAsync<TaskResponse>($"/evaluation/{task_id}", ct);

    public async Task<TaskResponse> WaitForTaskAsync(string type, string task_id, float pollSec=0.5f, CancellationToken ct = default)
    {
        while (true)
        {
            var t = type == TaskResponse.TaskType.Train
                ? await GetTrainingTaskAsync(task_id, ct)
                : await GetEvaluationTaskAsync(task_id,ct);

            if(t.state == TaskResponse.TaskState.Succeded|| t.state == TaskResponse.TaskState.Failed)
            {
                return t;
            }

            await Task.Delay(TimeSpan.FromSeconds(pollSec), ct);
        }
    }



    // ------------------------------------------------
    // GENERATION
    // ------------------------------------------------

    public Task<GenerateResponse> GenerateAsync(GenerateResponse req, CancellationToken ct = default)
    => PostJsonAsync<GenerateResponse>("/generate", req, ct);

    public Task<Dictionary<string, object>> PingAsync(CancellationToken ct = default)
        => GetAsync<Dictionary<string, object>>("/ping", ct);
}