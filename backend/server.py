# server.py
from __future__ import annotations
import asyncio, os, time
import httpx
from fastapi import FastAPI
from contextlib import asynccontextmanager

from middleware import setup_middleware
from db.session import create_db_and_tables
from routes import datasets, tasks, generate, models



PING_URL = os.getenv("PING_URL", "http://127.0.0.1:8000/ping")
PING_INTERVAL_SEC = 300  # tweak as needed

async def keep_alive_loop(stop: asyncio.Event, client: httpx.AsyncClient, url: str, interval: float):
    # Optional: add a small startup delay
    await asyncio.sleep(1)
    while not stop.is_set():
        try:
            # Add a timeout so it never hangs the loop
            await client.get(url, timeout=10.0)
        except asyncio.CancelledError:
            # Allow graceful cancellation
            break
        except Exception:
            # Swallow errors but don't spin; could add logging here
            pass
        # Sleep with cancellation awareness
        try:
            await asyncio.wait_for(stop.wait(), timeout=interval)
        except asyncio.TimeoutError:
            pass

def cleanup():
    # Perform any necessary cleanup here, like closing connections
    pass

@asynccontextmanager
async def lifespan(app: FastAPI):
    # DB setup
    create_db_and_tables()

    # Keep-alive setup
    app.state.stop_event = asyncio.Event()
    app.state.http = httpx.AsyncClient()
    app.state.keepalive_task = None

    if PING_URL:
        app.state.keepalive_task = asyncio.create_task(
            keep_alive_loop(app.state.stop_event, app.state.http, PING_URL, PING_INTERVAL_SEC)
        )

    yield

    # Cleanup
    app.state.stop_event.set()
    if app.state.keepalive_task:
        app.state.keepalive_task.cancel()
        try:
            await app.state.keepalive_task
        except asyncio.CancelledError:
            pass
    await app.state.http.aclose()
    cleanup()

app = FastAPI(lifespan=lifespan, title="NPD API", description="API for the NPD (New Product Development) system", version="1.0.0")

setup_middleware(app)


@app.get("/ping")
async def ping():
    return {"ok": True, "time": time.time()}

# Register routes
app.include_router(datasets.router)
app.include_router(tasks.router)
app.include_router(generate.router)
app.include_router(models.router)
