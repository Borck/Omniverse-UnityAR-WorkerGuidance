from fastapi import FastAPI
from app.core.logging import configure_logging
from app.omniverse.router import router as omniverse_router
from app.omniverse.nucleus_manager import get_manager
from app.unity.router import router as unity_router
from contextlib import asynccontextmanager
import omni.client


@asynccontextmanager
async def lifespan(app: FastAPI):
    """ Initializing the Omni-Unity Server and the Omniverse client connection."""
    # Seed the active Nucleus (applies its credentials) before connecting.
    get_manager()
    # Global setup (Omniverse Initialization)
    omni.client.initialize()
    yield
    omni.client.shutdown()


app = FastAPI(title="Omni-Unity Server", lifespan=lifespan)

app.include_router(omniverse_router, prefix="/omni", tags=["Omniverse Connection"])
app.include_router(unity_router, prefix="/unity", tags=["Unity Connection"])

logger = configure_logging("INFO")
