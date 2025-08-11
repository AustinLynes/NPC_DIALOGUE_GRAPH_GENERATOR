# routes/models.py
from __future__ import annotations
from typing import List, Dict, Any, Optional
from fastapi import APIRouter, Depends, HTTPException
from sqlmodel import Session, select
from models.api import (
    ModelCreate, ModelUpdate, ModelResponse, ModelVersionResponse,
    TrainRequest, Hyperparameters
)
from models.db import Model, ModelVersion, Dataset, Task
from db.session import get_session
import os, json, time, uuid, asyncio
from datetime import datetime

router = APIRouter()

MODELS_DIR = os.getenv("NPD_MODELS_DIR", "models")

def _ensure_models_dir():
    os.makedirs(MODELS_DIR, exist_ok=True)

def _to_model_response(m: Model) -> ModelResponse:
    return ModelResponse(
        id=m.id,
        model_id=m.model_id,
        description=m.description,
        type=m.type,  # type: ignore
        config=json.loads(m.config_json) if m.config_json else {},
        active_version_id=m.active_version_fk,
        created_at=m.created_at,
        updated_at=m.updated_at,
    )

def _to_version_response(v: ModelVersion) -> ModelVersionResponse:
    return ModelVersionResponse(
        id=v.id,
        model_fk=v.model_fk,
        version_tag=v.version_tag,
        dataset_id=v.dataset_id,
        state=v.state,  # type: ignore
        created_at=v.created_at,
        started_at=v.started_at,
        ended_at=v.ended_at,
        metrics=json.loads(v.metrics_json) if v.metrics_json else {},
        artifact_path=v.artifact_path,
        message=v.message,
    )

@router.post("/models", response_model=ModelResponse)
def create_model(req: ModelCreate, session: Session = Depends(get_session)):
    existing = session.exec(select(Model).where(Model.model_id == req.model_id)).first()
    if existing:
        raise HTTPException(status_code=409, detail="Model ID already exists")

    m = Model(
        model_id=req.model_id,
        description=req.description,
        type=req.type,  # type: ignore
        config_json=json.dumps(req.config) if req.config else None,
        active_version_fk=req.active_version_id,
        created_at=time.time(),
        updated_at=time.time(),
    )
    session.add(m)
    session.commit()
    session.refresh(m)
    return _to_model_response(m)

@router.get("/models", response_model=List[ModelResponse])
def list_models(session: Session = Depends(get_session)):
    rows = session.exec(select(Model)).all()
    return [_to_model_response(m) for m in rows]

@router.get("/models/{model_id}", response_model=ModelResponse)
def get_model(model_id: str, session: Session = Depends(get_session)):
    m = session.exec(select(Model).where(Model.model_id == model_id)).first()
    if not m:
        raise HTTPException(status_code=404, detail="Model not found")
    return _to_model_response(m)

@router.put("/models/{model_id}", response_model=ModelResponse)
def update_model(model_id: str, req: ModelUpdate, session: Session = Depends(get_session)):
    m = session.exec(select(Model).where(Model.model_id == model_id)).first()
    if not m:
        raise HTTPException(status_code=404, detail="Model not found")
    if req.description is not None:
        m.description = req.description
    if req.type is not None:
        m.type = req.type  # type: ignore
    if req.config is not None:
        m.config_json = json.dumps(req.config)
    if req.active_version_id is not None:
        m.active_version_fk = req.active_version_id
    m.updated_at = time.time()
    session.add(m)
    session.commit()
    session.refresh(m)
    return _to_model_response(m)

@router.delete("/models/{model_id}", response_model=Dict[str, str])
def delete_model(model_id: str, session: Session = Depends(get_session)):
    m = session.exec(select(Model).where(Model.model_id == model_id)).first()
    if not m:
        raise HTTPException(status_code=404, detail="Model not found")
    # Cascade delete versions (simple manual cascade)
    versions = session.exec(select(ModelVersion).where(ModelVersion.model_fk == m.id)).all()
    for v in versions:
        session.delete(v)
    session.delete(m)
    session.commit()
    return {"message": "Model deleted"}

@router.get("/models/{model_id}/versions", response_model=List[ModelVersionResponse])
def list_versions(model_id: str, session: Session = Depends(get_session)):
    m = session.exec(select(Model).where(Model.model_id == model_id)).first()
    if not m:
        raise HTTPException(status_code=404, detail="Model not found")
    vers = session.exec(select(ModelVersion).where(ModelVersion.model_fk == m.id)).all()
    return [_to_version_response(v) for v in vers]

@router.get("/models/{model_id}/versions/{version_id}", response_model=ModelVersionResponse)
def get_version(model_id: str, version_id: int, session: Session = Depends(get_session)):
    m = session.exec(select(Model).where(Model.model_id == model_id)).first()
    if not m:
        raise HTTPException(status_code=404, detail="Model not found")
    v = session.get(ModelVersion, version_id)
    if not v or v.model_fk != m.id:
        raise HTTPException(status_code=404, detail="Version not found")
    return _to_version_response(v)

@router.put("/models/{model_id}/versions/{version_id}/promote", response_model=ModelResponse)
def promote_version(model_id: str, version_id: int, session: Session = Depends(get_session)):
    m = session.exec(select(Model).where(Model.model_id == model_id)).first()
    if not m:
        raise HTTPException(status_code=404, detail="Model not found")
    v = session.get(ModelVersion, version_id)
    if not v or v.model_fk != m.id:
        raise HTTPException(status_code=404, detail="Version not found")
    m.active_version_fk = v.id
    m.updated_at = time.time()
    session.add(m)
    session.commit()
    session.refresh(m)
    return _to_model_response(m)

# Start a training task that will produce a ModelVersion
@router.post("/models/{model_id}/train", response_model=ModelVersionResponse)
def train_model(model_id: str, req: TrainRequest, session: Session = Depends(get_session)):
    # Verify model and dataset
    m = session.exec(select(Model).where(Model.model_id == model_id)).first()
    if not m:
        raise HTTPException(status_code=404, detail="Model not found")
    ds = session.exec(select(Dataset).where(Dataset.dataset_id == req.dataset_id)).first()
    if not ds:
        raise HTTPException(status_code=404, detail="Dataset not found")

    # Create queued model version
    v = ModelVersion(
        model_fk=m.id,
        version_tag=req.version_tag,
        dataset_id=req.dataset_id,
        state="queued",
        created_at=time.time(),
        message="Queued for training",
    )
    session.add(v)
    session.commit()
    session.refresh(v)

    # Create a training Task mapped to this (for UI continuity)
    task_id = uuid.uuid4().hex
    t = Task(
        task_id=task_id,
        type="train",
        state="queued",
        created_at=time.time(),
        model_tag=m.model_id,
        dataset_id=req.dataset_id,
        hyperparameters_json=req.hyperparameters.json(),
        metrics_json=json.dumps({}),
        history_json=json.dumps([]),
        message=f"Queued training for model {m.model_id} -> version {v.id}",
    )
    session.add(t)
    session.commit()

    # Launch background worker
    asyncio.create_task(_training_worker_for_model(m.id, v.id, req.hyperparameters.dict()))

    return _to_version_response(v)

async def _training_worker_for_model(model_pk: int, version_pk: int, hp: Dict[str, Any]) -> None:
    from db.session import new_session
    import math, random

    _ensure_models_dir()
    session = new_session()
    try:
        v = session.get(ModelVersion, version_pk)
        m = session.get(Model, model_pk)
        if not v or not m:
            return

        v.state = "running"
        v.started_at = time.time()
        v.message = "Training started"
        session.add(v)
        session.commit()

        # Simulated training (reuse your existing scheme)
        h = hp or {}
        seed = int(h.get("seed", 42))
        epochs = int(h.get("epochs", 5))
        random.seed(seed)
        history: List[Dict[str, float]] = []

        for epoch in range(epochs):
            await asyncio.sleep(0.2)
            frac = (epoch + 1) / epochs
            loss = 1.2 * math.exp(-2.2 * frac) + random.uniform(0.01, 0.05)
            acc = 0.4 + 0.6 * frac + random.uniform(-0.02, 0.02)
            ppl = math.exp(loss * 3.0)
            style = 0.5 + 0.5 * frac + random.uniform(-0.03, 0.03)
            history.append({"epoch": float(epoch + 1), "loss": float(loss), "accuracy": float(acc), "perplexity": float(ppl), "style_score": float(style)})
            v.message = f"Epoch {epoch + 1}/{epochs} - Loss {loss:.4f}, Acc {acc:.4f}"
            session.add(v)
            session.commit()

        # Finalize metrics from last epoch
        final = {k: v for k, v in history[-1].items() if k != "epoch"}
        v.state = "succeeded"
        v.ended_at = time.time()
        v.metrics_json = json.dumps(final)

        # Write artifact (stub file with metadata)
        model_dir = os.path.join(MODELS_DIR, m.model_id)
        os.makedirs(model_dir, exist_ok=True)
        artifact_path = os.path.join(model_dir, f"version_{version_pk}.json")
        with open(artifact_path, "w", encoding="utf-8") as f:
            json.dump({
                "model_id": m.model_id,
                "model_type": m.type,
                "version_id": version_pk,
                "version_tag": v.version_tag,
                "dataset_id": v.dataset_id,
                "config": json.loads(m.config_json) if m.config_json else {},
                "hyperparameters": h,
                "metrics": final,
                "created_at": v.created_at,
                "ended_at": v.ended_at,
            }, f, indent=2)
        v.artifact_path = artifact_path
        v.message = "Training completed"
        session.add(v)
        session.commit()
    except Exception as e:
        v = session.get(ModelVersion, version_pk)
        if v:
            v.state = "failed"
            v.ended_at = time.time()
            v.message = f"Training failed: {e}"
            session.add(v)
            session.commit()
    finally:
        session.close()
