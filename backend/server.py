# server.py
from __future__ import annotations
import asyncio, os, time
import httpx
from fastapi import FastAPI
from middleware import setup_middleware
from db.session import create_db_and_tables
from routes import datasets, tasks, generate

app = FastAPI(title="NPD API", description="API for the NPD (New Product Development) system", version="1.0.0")

setup_middleware(app)

@app.on_event("startup")
def on_startup():
    create_db_and_tables()

PING_URL = os.getenv("PING_URL", "http://127.0.0.1:8000/ping")

@app.on_event("startup")
async def start_keep_alive():
    async def keep_alive_loop():
        while True:
            try:
                async with httpx.AsyncClient() as client:
                    await client.get(PING_URL)
                await asyncio.sleep(300)
            except Exception:
                await asyncio.sleep(300)
    asyncio.create_task(keep_alive_loop())

@app.get("/ping")
async def ping():
    return {"ok": True, "time": time.time()}

# Register routes
app.include_router(datasets.router)
app.include_router(tasks.router)
app.include_router(generate.router)
