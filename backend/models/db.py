# models/db.py
from __future__ import annotations
from typing import Optional
from sqlmodel import SQLModel, Field
from datetime import datetime

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
    tags_json: Optional[str] = None

class Task(SQLModel, table=True):
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


def Model(SqlModel, table=True):
    id: Optional[int] = Field(default=None, primary_key=True)
    model_id:  str = Field(index=True, unique=True)
    description: Optional[str] = None
    type: str = "base" # "base" | "ensemble"
    config_json: Optional[str] = None
    active_version_fx: Optional[int] = Field(default=None, foreign_key="model_version.id")
    created_at: float = Field(default_factory=lambda: datetime.now().timestamp())
    updated_at: float = Field(default_factory=lambda: datetime.now().timestamp())

def ModelVersion(SqlModel, table=True):
    id: Optional[int] = Field(default=None, primary_key=True)
    model_fk: int = Field(foreign_key="model.id", index=True)
    version_tag: str = None
    state: str = "queued"  # "queued" | "running" | "succeeded" | "failed"
    created_at: float = Field(default_factory=lambda: datetime.now().timestamp())
    started_at: Optional[float] = None
    ended_at: Optional[float] = None
    metrics_json: Optional[str] = None
    artifact_path: Optional[str] = None
    message: str = ""