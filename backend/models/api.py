# models/api.py
from __future__ import annotations
from typing import Dict, List, Literal, Optional
from pydantic import BaseModel, Field

# --------- Dataset DTOs ---------
class DatasetSampleCreate(BaseModel):
    persona: str
    emotion: str
    text: str
    tags: Optional[List[str]] = None

class CreateDatasetRequest(BaseModel):
    dataset_id: str
    description: Optional[str] = None
    samples: Optional[List[DatasetSampleCreate]] = None

# --------- Task / Training / Eval DTOs ---------
class Hyperparameters(BaseModel):
    lr: float = 1e-4
    batch_size: int = 16
    epochs: int = 5
    seed: int = 42

TaskType = Literal["train", "evaluate"]
TaskState = Literal["queued", "running", "succeeded", "failed"]

class TrainRequest(BaseModel):
    model_tag: str = "baseline_stub_v0"
    dataset_id: str
    hyperparameters: Hyperparameters = Hyperparameters()

class EvaluationRequest(BaseModel):
    model_tag: str
    dataset_id: str
    metrics: List[Literal["loss", "accuracy", "perplexity", "style_score"]] = ["loss", "accuracy"]

class TaskResponse(BaseModel):
    task_id: str
    type: TaskType
    state: TaskState
    created_at: float
    started_at: Optional[float] = None
    ended_at: Optional[float] = None
    progress: float = 0.0
    message: str = ""
    model_tag: str
    dataset_id: Optional[str] = None
    hyperparameters: Optional[Hyperparameters] = None
    metrics: Dict[str, float] = Field(default_factory=dict)
    history: List[Dict[str, float]] = Field(default_factory=list)

# --------- Generation DTOs ---------
class GenerateRequest(BaseModel):
    model_tag: str = "baseline_stub_v0"
    persona: str
    emotion: str
    context: Optional[List[str]] = None
    num_candidates: int = 3
    seed: int = 123

class Candidate(BaseModel):
    text: str
    score: float

class GenerateResponse(BaseModel):
    candidates: List[Candidate]
