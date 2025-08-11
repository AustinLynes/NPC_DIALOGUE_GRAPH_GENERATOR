# routes/tasks.py
from __future__ import annotations
from typing import Dict, List
from fastapi import APIRouter, Depends, HTTPException
from sqlmodel import Session, select
from models.api import TrainRequest, EvaluationRequest, TaskResponse, Hyperparameters
from models.db import Task, Dataset
from db.session import get_session, new_session
import asyncio, uuid, time, math, random, json

router = APIRouter()

# ---------- Public Endpoints ----------

@router.post("/training/start", response_model=TaskResponse)
async def start_training(req: TrainRequest, session: Session = Depends(get_session)):
    # Validate dataset exists
    ds = session.exec(select(Dataset).where(Dataset.dataset_id == req.dataset_id)).first()
    if not ds:
        raise HTTPException(status_code=404, detail="Dataset not found")

    task_id = uuid.uuid4().hex
    now = time.time()

    task = Task(
        task_id=task_id,
        type="train",
        state="queued",
        created_at=now,
        started_at=None,
        ended_at=None,
        progress=0.0,
        message="Queued for training",
        model_tag=req.model_tag,
        dataset_id=req.dataset_id,
        hyperparameters_json=req.hyperparameters.json(),
        metrics_json=json.dumps({}),
        history_json=json.dumps([]),
    )
    session.add(task)
    session.commit()

    asyncio.create_task(_temp_training_worker(task_id))
    return _task_to_response(task)

@router.get("/training/{task_id}", response_model=TaskResponse)
def get_training_task(task_id: str, session: Session = Depends(get_session)):
    row = session.get(Task, task_id)
    if not row:
        raise HTTPException(status_code=404, detail="Task not found")
    return _task_to_response(row)

@router.post("/evaluation/start", response_model=TaskResponse)
async def start_evaluation(req: EvaluationRequest, session: Session = Depends(get_session)):
    # Validate dataset exists
    ds = session.exec(select(Dataset).where(Dataset.dataset_id == req.dataset_id)).first()
    if not ds:
        raise HTTPException(status_code=404, detail="Dataset not found")

    task_id = uuid.uuid4().hex
    now = time.time()

    task = Task(
        task_id=task_id,
        type="evaluate",
        state="queued",
        created_at=now,
        started_at=None,
        ended_at=None,
        progress=0.0,
        message="Queued for evaluation",
        model_tag=req.model_tag,
        dataset_id=req.dataset_id,
        hyperparameters_json=None,
        metrics_json=json.dumps({}),
        history_json=json.dumps([]),
    )
    session.add(task)
    session.commit()

    asyncio.create_task(_temp_evaluation_worker(task_id))
    return _task_to_response(task)

@router.get("/evaluation/{task_id}", response_model=TaskResponse)
def get_evaluation_task(task_id: str, session: Session = Depends(get_session)):
    row = session.get(Task, task_id)
    if not row:
        raise HTTPException(status_code=404, detail="Task not found")
    return _task_to_response(row)

# ---------- Background Workers ----------

async def _temp_training_worker(task_id: str) -> None:
    session = new_session()
    try:
        row = session.get(Task, task_id)
        if not row:
            return
        row.state = "running"
        row.started_at = time.time()
        hp = Hyperparameters.parse_raw(row.hyperparameters_json) if row.hyperparameters_json else Hyperparameters()
        session.add(row)
        session.commit()

        random.seed(hp.seed)
        history: List[Dict[str, float]] = []

        for epoch in range(hp.epochs):
            await asyncio.sleep(0.2)
            frac = (epoch + 1) / hp.epochs
            loss = 1.2 * math.exp(-2.2 * frac) + random.uniform(0.01, 0.05)
            acc = 0.4 + 0.6 * frac + random.uniform(-0.02, 0.02)
            ppl = math.exp(loss * 3.0)
            style = 0.5 + 0.5 * frac + random.uniform(-0.03, 0.03)

            history.append({"epoch": float(epoch + 1), "loss": float(loss), "accuracy": float(acc), "perplexity": float(ppl), "style_score": float(style)})

            row.progress = frac
            row.message = f"Epoch {epoch + 1}/{hp.epochs} - Loss: {loss:.4f}, Accuracy: {acc:.4f}, Perplexity: {ppl:.4f}, Style Score: {style:.4f}"
            row.history_json = json.dumps(history)
            session.add(row)
            session.commit()

        last = {k: v for k, v in history[-1].items() if k != "epoch"}
        row.state = "succeeded"
        row.ended_at = time.time()
        row.message = "Training completed"
        row.metrics_json = json.dumps(last)
        session.add(row)
        session.commit()
    except Exception as e:
        row = session.get(Task, task_id)
        if row:
            row.state = "failed"
            row.ended_at = time.time()
            row.message = f"Training failed: {e}"
            session.add(row)
            session.commit()
    finally:
        session.close()

async def _temp_evaluation_worker(task_id: str) -> None:
    session = new_session()
    try:
        row = session.get(Task, task_id)
        if not row:
            return
        row.state = "running"
        row.started_at = time.time()
        session.add(row)
        session.commit()

        steps = 5
        for i in range(steps):
            await asyncio.sleep(0.15)
            row.progress = (i + 1) / steps
            row.message = f"Evaluating {row.model_tag} on {row.dataset_id} - Step {i + 1}/{steps}"
            session.add(row)
            session.commit()

        # Stable-ish seed derived from identifiers
        seed_val = abs(hash((row.model_tag, row.dataset_id))) % (2**32)
        random.seed(seed_val)
        loss = random.uniform(0.2, 0.6)
        acc = random.uniform(0.7, 0.9)
        ppl = math.exp(loss * 2.5)
        style = random.uniform(0.65, 0.9)

        row.metrics_json = json.dumps({"loss": float(loss), "accuracy": float(acc), "perplexity": float(ppl), "style_score": float(style)})
        row.state = "succeeded"
        row.message = "Evaluation completed"
        row.ended_at = time.time()
        session.add(row)
        session.commit()
    except Exception as e:
        row = session.get(Task, task_id)
        if row:
            row.state = "failed"
            row.ended_at = time.time()
            row.message = f"Evaluation failed: {e}"
            session.add(row)
            session.commit()
    finally:
        session.close()

# ---------- Mappers ----------

def _task_to_response(row: Task) -> TaskResponse:
    return TaskResponse(
        task_id=row.task_id,
        type=row.type,  # type: ignore
        state=row.state,  # type: ignore
        created_at=row.created_at,
        started_at=row.started_at,
        ended_at=row.ended_at,
        progress=row.progress,
        message=row.message,
        model_tag=row.model_tag,
        dataset_id=row.dataset_id,
        hyperparameters=Hyperparameters.parse_raw(row.hyperparameters_json) if row.hyperparameters_json else None,
        metrics=json.loads(row.metrics_json) if row.metrics_json else {},
        history=json.loads(row.history_json) if row.history_json else [],
    )
