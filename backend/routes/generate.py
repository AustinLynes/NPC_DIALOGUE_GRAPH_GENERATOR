# routes/generate.py
from __future__ import annotations
from fastapi import APIRouter
from models.api import GenerateRequest, GenerateResponse, Candidate
import random

router = APIRouter()

@router.post("/generate", response_model=GenerateResponse)
async def generate_text(req: GenerateRequest):
    random.seed(req.seed)
    ctx = " ".join(req.context or [])
    base = f"[{req.model_tag}] {req.persona} {req.emotion}"
    variants = [
        "speaks softly about",
        "reflects on",
        "murmurs regarding",
        "broods over",
        "admits concerning",
    ]

    candidates = [
        Candidate(
            text=f"{base} {random.choice(variants)} {ctx or 'the current situation.'}".strip(),
            score=round(0.7 + random.random() * 0.3, 3),
        )
        for _ in range(req.num_candidates)
    ]
    return GenerateResponse(candidates=candidates)
