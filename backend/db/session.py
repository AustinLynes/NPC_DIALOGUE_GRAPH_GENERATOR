from __future__ import annotations
from typing import Generator
from sqlmodel import SQLModel, create_engine, Session
import os

DB_FILE = os.getenv("NPD_DB_FILE", "npd.sqlite")
engine = create_engine(f"sqlite:///{DB_FILE}", echo=False, connect_args={"check_same_thread": False})

def create_db_and_tables() -> None:
    SQLModel.metadata.create_all(engine)

# FastAPI dependency (yields and closes automatically)
def get_session() -> Generator[Session, None, None]:
    with Session(engine) as session:
        yield session

# For background tasks where Depends can't be used
def new_session() -> Session:
    return Session(engine)
