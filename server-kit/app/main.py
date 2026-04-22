from fastapi import FastAPI
from app.core.config import SERVER
from app.core.logging import configure_logging
from app.omniverse.router import router as omniverse_router
from app.unity.router import router as unity_router
from contextlib import asynccontextmanager
import omni.client


@asynccontextmanager
async def lifespan(app: FastAPI):
    """ Initializing the Omni-Unity Server and the Omniverse client connection."""
    # Global setup (Omniverse Initialization)
    omni.client.initialize()
    yield
    omni.client.shutdown()


app = FastAPI(title="Omni-Unity Server", lifespan=lifespan)

app.include_router(omniverse_router, prefix="/omni", tags=["Omniverse Connection"])
app.include_router(unity_router, prefix="/unity", tags=["Unity Connection"])

logger = configure_logging("INFO")
