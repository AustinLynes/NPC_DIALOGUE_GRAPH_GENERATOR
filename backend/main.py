from __future__ import annotations

import os
import asyncio
import math
import random
import time
import uuid
from typing import Any, Dict, List, Literal, Optional

from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel, Field

import httpx


title = "NPD API"
description = "API for the NPD (New Product Development) system"
version = "1.0.0"

app = FastAPI(title=title, description=description, version=version)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

class DatasetSample(BaseModel):
    persona: str = Field(..., min_length=1)
    emotion: str = Field(..., min_length=1)
    text: str = Field(..., min_length=1)
    tags: Optional[List[str]] = None

class Dataset(BaseModel):
    dataset_id: str
    description: Optional[str] = None
    samples: List[DatasetSample] = Field(default_factory=list)

DATASETS: Dict[str, Dataset] = {}  # dataset_id -> Dataset

class Hyperparameters(BaseModel):
    lr: float = 1e-4
    batch_size: int = 16
    epochs: int = 5
    seed: int = 42

class TrainRequest(BaseModel):
    model_tag: str = "baseline_stub_v0"
    dataset_id: str
    hyperparameters: Hyperparameters = Hyperparameters()

class EvaluationRequest(BaseModel):
    model_tag: str
    dataset_id: str
    metrics: List[Literal["loss", "accuracy", "perplexity", "style_score"]] = ["loss", "accuracy"]

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

JobType = Literal["train", "evaluate"]
JobState = Literal["queued", "running", "succeded", "failed"]

class Job(BaseModel):
    job_id: str
    type: JobType
    state: JobState = "queued"
    created_at: float = Field(default_factory=lambda: time.time())
    started_at: Optional[float] = None
    ended_at: Optional[float] = None
    progress: float = 0.0 # 0.0 -> 1.0
    message: str = ""
    model_tag: str
    dataset_id: Optional[str] = None
    Hyperparameters: Optional[Hyperparameters] = None
    metrics: Dict[str, float] = Field(default_factory=dict) 
    history: List[Dict[str, float]] = Field(default_factory=list)

JOBS: Dict[str, Job] = {}  # In-memory storage for jobs

@app.get("/ping")
async def ping():
    return {"ok": True, "time": time.time()}


ping_url = os.getenv("PING_URL", "http://127.0.0.1:8000/ping")

@app.on_event("startup")
async def start_keep_alive():
    async def keep_alive_loop():
        while True:
            try:
                async with httpx.AsyncClient() as client:
                    await client.get(ping_url)
                await asyncio.sleep(300)  # 5 minutes
            except Exception:
                await asyncio.sleep(300)

    asyncio.create_task(keep_alive_loop())


# ----------------------------------
# DATASET ENDPOINTS
# ----------------------------------

"""
# HTTP Status Codes

# 100 Continue
# 101 Switching Protocols
# 102 Processing
# 103 Early Hints

# 200 OK
# 201 Created
# 202 Accepted
# 203 Non-Authoritative Information
# 204 No Content
# 205 Reset Content
# 206 Partial Content
# 207 Multi-Status
# 208 Already Reported
# 226 IM Used

# 300 Multiple Choices
# 301 Moved Permanently
# 302 Found
# 303 See Other
# 304 Not Modified
# 305 Use Proxy
# 306 Switch Proxy
# 307 Temporary Redirect
# 308 Permanent Redirect

# 400 Bad Request
# 401 Unauthorized
# 402 Payment Required
# 403 Forbidden
# 404 Not Found
# 405 Method Not Allowed
# 406 Not Acceptable
# 407 Proxy Authentication Required
# 408 Request Timeout
# 409 Conflict
# 410 Gone
# 411 Length Required
# 412 Precondition Failed
# 413 Payload Too Large
# 414 URI Too Long
# 415 Unsupported Media Type
# 416 Range Not Satisfiable
# 417 Expectation Failed
# 418 I'm a teapot
# 421 Misdirected Request
# 422 Unprocessable Entity
# 423 Locked
# 424 Failed Dependency
# 425 Too Early
# 426 Upgrade Required
# 428 Precondition Required
# 429 Too Many Requests
# 431 Request Header Fields Too Large
# 451 Unavailable For Legal Reasons

# 500 Internal Server Error
# 501 Not Implemented
# 502 Bad Gateway
# 503 Service Unavailable
# 504 Gateway Timeout
# 505 HTTP Version Not Supported
# 506 Variant Also Negotiates
# 507 Insufficient Storage
# 508 Loop Detected
# 510 Not Extended
# 511 Network Authentication Required
"""

class CreateDatasetRequest(BaseModel):
    dataset_id: str
    description: Optional[str] = None
    samples: Optional[List[DatasetSample]] = []

@app.post("/datasets", response_model=Dataset)
def create_dataset(req: CreateDatasetRequest):
    if req.dataset_id in DATASETS:
        raise HTTPException(status_code=409, detail="Dataset ID already exists")
    
    ds = Dataset(dataset_id=req.dataset_id, description=req.description, samples=req.samples)
    DATASETS[req.dataset_id] = ds
    return ds

@app.get("/datasets/{dataset_id}", response_model=Dataset)
def get_dataset(dataset_id: str):
    ds = DATASETS.get(dataset_id)
    if not ds:
        raise HTTPException(status_code=404, detail="Dataset not found")
    return ds

@app.post("/datasets/{dataset_id}/samples", response_model=Dataset)
def add_samples_to_dataset(dataset_id: str, samples: List[DatasetSample]):
    ds = DATASETS.get(dataset_id)
    if not ds:
        raise HTTPException(status_code=404, detail="Dataset not found")
    before = len(ds.samples)
    ds.samples.extend(samples)
    return { "added": len(ds.samples) - before, "total": len(ds.samples) }


@app.get("/datasets", response_model=List[Dataset])
def list_datasets():
    return list(DATASETS.values())


# ------------------------------------------
# TEMPORARY JOB HELPERS.
# ------------------------------------------

async def _temp_training_job(job: Job):
    try:
        job.state = "running"
        job.started_at = time.time()
        hp = job.Hyperparameters or Hyperparameters()
        random.seed(hp.seed)

        for epoch in range(hp.epochs):
            await asyncio.sleep(0.2)  # Simulate training time
            frac = (epoch + 1) / hp.epochs
            loss = 1.2 * math.exp(-2.2 * frac) + random.uniform(0.01, 0.05)
            acc = 0.4 + 0.6 * frac + random.uniform(-0.02, 0.02)
            ppl = math.exp(loss * 3.0)
            style = 0.5 + 0.5 * frac + random.uniform(-0.03, 0.03)

            job.history.append({"epoch": epoch + 1, "loss": loss, "accuracy": acc, "perplexity": ppl, "style_score": style})
            job.progress = frac
            job.message = f"Epoch {epoch + 1}/{hp.epochs} - Loss: {loss:.4f}, Accuracy: {acc:.4f}, Perplexity: {ppl:.4f}, Style Score: {style:.4f}"

        last = job.history[-1]
        job.metrics = {k: v for k, v in last.items() if k != "epoch"}
        job.state = "succeded"
        job.ended_at = time.time()
        job.message = "Training Completed"
        
    except Exception as e:
        job.state = "failed"
        job.message = f"Training failed: {str(e)}"
        job.ended_at = time.time()

async def _temp_evaluation_job(job: Job):
    try: 
        job.state = "running"
        job.stated_at = time.time()
        
        steps = 5
        for i in range(steps):
            await asyncio.sleep(0.15)  # Simulate evaluation time
            job.progress = (i + 1) / steps
            job.message = f"Evaluating {job.model_tag} on {job.dataset_id} - Step {i + 1}/{steps}"

        random.seed(hash(job.model_tag, job.dataset_id) % (2**32))
        loss = random.uniform(0.2, 0.6)
        acc = random.uniform(0.7, 0.9)
        ppl = math.exp(loss * 2.5)
        style = random.uniform(0.65, 0.9)

   
        job.metrics = {"loss": loss, "accuracy": acc, "perplexity": ppl, "style_score": style}
        job.state = "succeeded"
        job.message = "evaluation complete"
        job.ended_at = time.time()
    except Exception as e:
        job.state = "failed"
        job.message = f"error: {e}"
        job.ended_at = time.time()


@app.post("/training/start", response_model=Job)
async def start_training(req: TrainRequest):
    if req.dataset_id not in DATASETS:
        raise HTTPException(status_code=404, detail="Dataset not found")
    job_id = str(uuid.uuid4()).hex
    job = Job(
        job_id= job_id,
        type="train",
        model_tag=req.model_tag, 
        dataset_id=req.dataset_id, 
        Hyperparameters=req.hyperparameters,
        state="queued",
        message="queued for training",
    )
    JOBS[job_id] = job
    asyncio.create_task(_temp_training_job(job))
    return 

@app.get("/training/{job_id}", response_model=Job)
def get_training_job(job_id: str):
    job = JOBS.get(job_id)
    if not job:
        raise HTTPException(status_code=404, detail="Job not found")
    return job

@app.post("/evaluation/start", response_model=Job)
async def start_evaluation(req: EvaluationRequest):
    if req.dataset_id not in DATASETS:
        raise HTTPException(status_code=404, detail="Dataset not found")
    job_id = str(uuid.uuid4()).hex
    job = Job(
        job_id=job_id,
        type="evaluate",
        model_tag=req.model_tag,
        dataset_id=req.dataset_id,
        state="queued",
        message="queued for evaluation",
    )
    JOBS[job_id] = job
    asyncio.create_task(_temp_evaluation_job(job))
    return job


@app.post("/generate", response_model=GenerateResponse)
async def generate_text(req: GenerateRequest):
    random.seed(req.seed)
    ctx = " ".join(req.context or [])
    base = f"[{req.model_tag}] {req.persona} {req.emotion} "
    candidates = []
    for i in range(req.num_candidates):
        variant = random.choice(
            [
                "speaks softly about",
                "reflects on",
                "murmurs regarding",
                "broods over",
                "admits concerning",
            ]
        )
        text = f"{base} {variant} {ctx or 'the current situation.'}".strip()
        score = round(0.7 + random.random() * 0.3, 3)
        candidates.append(Candidate(text=text, score=score))

    return GenerateResponse(candidates=candidates)