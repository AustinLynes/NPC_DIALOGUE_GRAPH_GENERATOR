# routes/datasets.py
from __future__ import annotations
from typing import List, Dict
from fastapi import APIRouter, Depends, HTTPException
from sqlmodel import Session, select
from models.api import CreateDatasetRequest, DatasetSampleCreate
from models.db import Dataset, DatasetSample
from db.session import get_session
import json

router = APIRouter()

@router.post("/datasets", response_model=Dataset)
def create_dataset(req: CreateDatasetRequest, session: Session = Depends(get_session)):
    existing = session.exec(select(Dataset).where(Dataset.dataset_id == req.dataset_id)).first()
    if existing:
        raise HTTPException(status_code=409, detail="Dataset ID already exists")

    ds = Dataset(dataset_id=req.dataset_id, description=req.description)
    session.add(ds)
    session.commit()
    session.refresh(ds)

    if req.samples:
        _add_samples(session, ds.id, req.samples)

    return ds

@router.get("/datasets/{dataset_id}", response_model=Dataset)
def get_dataset(dataset_id: str, session: Session = Depends(get_session)):
    ds = session.exec(select(Dataset).where(Dataset.dataset_id == dataset_id)).first()
    if not ds:
        raise HTTPException(status_code=404, detail="Dataset not found")
    return ds

@router.post("/datasets/{dataset_id}/samples", response_model=Dict[str, int])
def add_samples_to_dataset(dataset_id: str, samples: List[DatasetSampleCreate], session: Session = Depends(get_session)):
    ds = session.exec(select(Dataset).where(Dataset.dataset_id == dataset_id)).first()
    if not ds:
        raise HTTPException(status_code=404, detail="Dataset not found")

    before = session.exec(
        select(DatasetSample).where(DatasetSample.dataset_fk == ds.id)
    ).all()
    before_count = len(before)

    _add_samples(session, ds.id, samples)

    after_count = session.exec(
        select(DatasetSample).where(DatasetSample.dataset_fk == ds.id)
    ).count()  # SQLModel supports count() on queries (returns int) in newer versions

    return {"added": after_count - before_count, "total": after_count}

@router.get("/datasets", response_model=List[Dataset])
def list_datasets(session: Session = Depends(get_session)):
    return session.exec(select(Dataset)).all()

def _add_samples(session: Session, dataset_pk: int, samples: List[DatasetSampleCreate]) -> None:
    for s in samples:
        row = DatasetSample(
            dataset_fk=dataset_pk,
            persona=s.persona,
            emotion=s.emotion,
            text=s.text,
            tags_json=json.dumps(s.tags) if s.tags is not None else None,
        )
        session.add(row)
    session.commit()
