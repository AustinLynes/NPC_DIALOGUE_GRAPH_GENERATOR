using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using Unity.VisualScripting;

[Serializable]
public class DatasetSample
{
    public int id;
    public string persona;
    public string emotion;
    public string text;
    public List<string> tags;
}

[Serializable]
public class DatasetSampleUpdate
{
    public string persona;
    public string emotion;
    public string text;
    public List<string> tags;
}

[Serializable]
public class DatasetSampleOut
{
    public int id;
    public string persona;
    public string emotion;
    public string text;
    public List<string> tags;
}
[Serializable]
public class CreateDatasetRequest
{
    public string dataset_id;
    public string description;
    public List<DatasetSample> samples;
}

[Serializable]
public class UpdateDatasetRequest
{
    public string description = ""; // OPTIONAL
}

[Serializable]
public class Dataset
{
    public int id;            // PK
    public string dataset_id; // EXTERNAL ID
    public string description;
}

[Serializable]
public class Hyperparamaters
{
    public double lr = 1e-4;
    public int batch_size = 16;
    public int epochs = 5;
    public int seed = 42;
}

[Serializable]
public class TrainRequest
{
    public string model_tag = "baseline_stub_v0";
    public string dataset_id;
    public Hyperparamaters hyperparamaters = new Hyperparamaters();
}


[Serializable]
public class EvaluationRequest
{
    public string model_tag;
    public string dataset_id;
    public List<string> metrics = new List<string>() { "loss", "accuracy" };
}



[Serializable]
public class TaskResponse
{
    public string task_id;
    public string type;     // "train"  | "evaluate"
    public string state;    // "queued" | "running" | "succeded" | "failed"
    public double createed_at;
    public double? started_at;
    public double? ended_at;
    public float progress;
    public string message;
    public string model_tag;
    public string dataset_id;
    public Hyperparamaters hyperparameters;
    public Dictionary<string, float> metrics;
    public List<Dictionary<string, float>> history;

    public class TaskType {
        public const string Train = "train";
        public const string Evaluate = "evaluate";
    }
    
    public class TaskState
    {
        public const string Queued = "queued";
        public const string Running = "running";
        public const string Succeded = "succeded";
        public const string Failed = "failed";
    }


}

[Serializable]
public class GenerateRequest
{
    public string model_tag = "baseline_stub_v0";
    public string persona;
    public string emotion;
    public List<string> context;
    public int num_candidates = 3;
    public int seed = 123;
}

[Serializable]
public class Candidate
{
    public string text;
    public float score;
}

[Serializable]
public class GenerateResponse
{
    public List<Candidate> candidates;
}
