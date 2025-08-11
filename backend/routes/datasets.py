# routes/datasets.py
from __future__ import annotations
from typing import List, Dict
from fastapi import APIRouter, Depends, HTTPException
from sqlmodel import Session, select
from models.api import CreateDatasetRequest, DatasetSampleCreate, UpdateDatasetRequest, DatasetSampleUpdate, DatasetSampleOut
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

@router.get("/datasets", response_model=List[Dataset])
def list_datasets(session: Session = Depends(get_session)):
    return session.exec(select(Dataset)).all()

@router.get("/datasets/{dataset_id}", response_model=Dataset)
def get_dataset(dataset_id: str, session: Session = Depends(get_session)):
    ds = session.exec(select(Dataset).where(Dataset.dataset_id == dataset_id)).first()
    if not ds:
        raise HTTPException(status_code=404, detail="Dataset not found")
    return ds

@router.delete("/datasets/{dataset_id}", response_model=Dict[str, str])
def delete_dataset(dataset_id: str, session: Session = Depends(get_session)):
    ds = session.exec(select(Dataset).where(Dataset.dataset_id == dataset_id)).first()
    if not ds:
        raise HTTPException(status_code=404, detail="Dataset not found")
    
    session.exec(
        select(DatasetSample).where(DatasetSample.dataset_fk == ds.id)
    ).delete()
        
    session.delete(ds)
    session.commit()
    return {"message": "Dataset deleted successfully"}

@router.put("/datasets/{dataset_id}", response_model=Dataset)
def update_dataset(dataset_id: str, req: UpdateDatasetRequest, session: Session = Depends(get_session)):
    ds = session.exec(select(Dataset).where(Dataset.dataset_id == dataset_id)).first()
    if not ds:
        raise HTTPException(status_code=404, detail="Dataset not found")
    if req.description is not None:
        ds.description = req.description
    session.add(ds)
    session.commit()
    session.refresh(ds)
    return ds

# ################################
# REGION SAMPLES
# ################################

@router.get("/datasets/{dataset_id}/samples", response_model=List[DatasetSampleOut])
def get_dataset_samples(dataset_id: str, session: Session = Depends(get_session)):
    ds = session.exec(select(Dataset).where(Dataset.dataset_id == dataset_id)).first()
    if not ds:
        raise HTTPException(status_code=404, detail="Dataset not found")
    
    rows = session.exec(select(DatasetSample).where(DatasetSample.dataset_fk == ds.id)).all()
    
    return [
        DatasetSampleOut(
            id=r.id,
            persona=r.persona,
            emotion=r.emotion,
            text=r.text,
            tags=json.loads(r.tags_json) if r.tags_json else None,
        )
        for r in rows
    ]

# SINGLE SAMPLE OPERATIOS
@router.post("/datasets/{dataset_id}/samples/new", response_model=DatasetSampleOut)
def create_sample(dataset_id: str, sample: DatasetSampleCreate, session: Session = Depends(get_session)):
    ds = session.exec(select(Dataset).where(Dataset.dataset_id == dataset_id)).first()
    if not ds:
        raise HTTPException(status_code=404, detail="Dataset not found")

    row = DatasetSample(
        dataset_fk=ds.id,
        persona=sample.persona,
        emotion=sample.emotion,
        text=sample.text,
        tags_json=json.dumps(sample.tags) if sample.tags is not None else None,
    )
    session.add(row)
    session.commit()
    session.refresh(row)

    return DatasetSampleOut(
        id=row.id,
        persona=row.persona,
        emotion=row.emotion,
        text=row.text,
        tags=json.loads(row.tags_json) if row.tags_json else None
    )

@router.get("/datasets/{dataset_id}/samples/{sample_id}", response_model=DatasetSampleOut)
def get_sample(dataset_id: str, sample_id: int, session: Session = Depends(get_session)):
    ds = session.exec(select(Dataset).where(Dataset.dataset_id == dataset_id)).first()
    if not ds:
        raise HTTPException(status_code=404, detail="Dataset not found")
    
    row = session.get(DatasetSample, sample_id)
    if not row or row.dataset_fk != ds.id:
        raise HTTPException(status_code=404, detail="Sample not found in this dataset")
    
    return DatasetSampleOut(
        id=row.id,
        persona=row.persona,
        emotion=row.emotion,
        text=row.text,
        tags=json.loads(row.tags_json) if row.tags_json else None
    )

@router.put("/datasets/{dataset_id}/samples/{sample_id}", response_model=DatasetSampleOut)
def update_sample(dataset_id: str, sample_id: int, update: DatasetSampleUpdate, session: Session = Depends(get_session)):
    ds = session.exec(select(Dataset).where(Dataset.dataset_id == dataset_id)).first()
    if not ds:
        raise HTTPException(status_code=404, detail="Dataset not found")
    row = session.get(DatasetSample, sample_id)
    if not row or row.dataset_fk != ds.id:
        raise HTTPException(status_code=404, detail="Sample not found in this dataset")
    if update.persona is not None:
        row.persona = update.persona
    if update.emotion is not None:
        row.emotion = update.emotion
    if update.text is not None:
        row.text = update.text
    if update.tags is not None:
        row.tags_json = json.dumps(update.tags) if update.tags else None
    
    session.add(row)
    session.commit()
    session.refresh(row)
    return DatasetSampleOut(
        id=row.id,
        persona=row.persona,
        emotion=row.emotion,
        text=row.text,
        tags=json.loads(row.tags_json) if row.tags_json else None
    )

@router.delete("/datasets/{dataset_id}/samples/{sample_id}", response_model=Dict[str, str])
def delete_sample(dataset_id: str, sample_id: int, session: Session = Depends(get_session)):
    ds = session.exec(select(Dataset).where(Dataset.dataset_id == dataset_id)).first()
    if not ds:
        raise HTTPException(status_code=404, detail="Dataset not found")
    row = session.get(DatasetSample, sample_id)
    if not row or row.dataset_fk != ds.id:
        raise HTTPException(status_code=404, detail="Sample not found in this dataset")
    session.delete(row)
    session.commit()
    return {"message": "Sample deleted successfully"}



# def _add_samples(session: Session, dataset_pk: int, samples: List[DatasetSampleCreate]) -> None:
#     for s in samples:
#         row = DatasetSample(
#             dataset_fk=dataset_pk,
#             persona=s.persona,
#             emotion=s.emotion,
#             text=s.text,
#             tags_json=json.dumps(s.tags) if s.tags is not None else None,
#         )
#         session.add(row)
#     session.commit()
