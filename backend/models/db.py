# models/db.py
from __future__ import annotations
from typing import Optional
from sqlmodel import SQLModel, Field

class Dataset(SQLModel, table=True):
    id: Optional[int] = Field(default=None, primary_key=True)
    # External identifier
    dataset_id: str = Field(index=True, unique=True)
    description: Optional[str] = None

class DatasetSample(SQLModel, table=True):
    id: Optional[int] = Field(default=None, primary_key=True)
    dataset_fk: int = Field(foreign_key="dataset.id", index=True)
    persona: str
    emotion: str
    text: str
    # JSON stored as string for SQLite simplicity
    tags_json: Optional[str] = None

class Task(SQLModel, table=True):
    # External identifier for tasks
    task_id: str = Field(primary_key=True)
    type: str  # "train" | "evaluate"
    state: str  # "queued" | "running" | "succeeded" | "failed"
    created_at: float
    started_at: Optional[float] = None
    ended_at: Optional[float] = None
    progress: float = 0.0
    message: str = ""
    model_tag: str
    # We store the external dataset_id string for convenience
    dataset_id: Optional[str] = None
    # JSON blobs (serialized)
    hyperparameters_json: Optional[str] = None
    metrics_json: Optional[str] = None
    history_json: Optional[str] = None
