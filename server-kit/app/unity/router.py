"""FastAPI entrypoint for guidance runtime HTTP and bridge endpoints."""
import os
import json
from contextlib import asynccontextmanager
from fastapi import FastAPI, APIRouter
from fastapi import BackgroundTasks
from fastapi import HTTPException
from pydantic import BaseModel
from fastapi.responses import FileResponse
from fastapi.responses import JSONResponse
from fastapi import status
from pathlib import Path
from fastapi import WebSocket, WebSocketDisconnect
from fastapi.responses import FileResponse
from app.core.logging import configure_logging




router = APIRouter(tags=["Unity Connection"])
logger = configure_logging("INFO")



# Asset Directory file for storing assets. We can replace it with an S3 or cloud storage.
ASSET_DIR = Path(__file__).resolve().parent / "assets"


connected_clients = []   # keeps track of Unity devices that are connected


@router.get("/health")
def health() -> dict[str, str]:
    logger.info("logger connected")
    return {"status": "ok"}


# A simple get API call to download a GLB file from the server(right now locally) to Unity
@router.get("/assets/{step_id}")
async def get_asset_test(step_id: str):
    path = os.path.join(ASSET_DIR, f"{step_id}.glb")
    logger.debug(f"Attempting to retrieve asset from: {path}")
    if not os.path.exists(path):
      print(f"FAILED to find asset at: {path}")
      logger.warning(f"FAILED to find asset at: {path}")
      return JSONResponse(status_code=404, content={'error': f'Asset not found with id :{step_id}'})
    logger.info(f"Serving asset: {path}")
    return FileResponse(path, media_type="model/gltf-binary")


@router.websocket('/ws')
async def websocket_endpoint(websocket: WebSocket):
    # accept incoming connection from unity
    await websocket.accept()
    connected_clients.append(websocket)
    print(f"Unity client must've connected: {len(connected_clients)}")
    logger.info(f"Unity client connected. Total clients: {len(connected_clients)}")
    await websocket.send_text(json.dumps({"action": "load_step", "step_id": "step_001"}))


    try:
      # keep listening for msges from unity
      raw = "" # Initialize raw to an empty string to prevent Pylance warning
      while True:
        raw = await websocket.receive_text()
        data = json.loads(raw)
        print(f"Data received from unity is {data}")
        logger.info(f"Data received from Unity: {data}")


        # unity sends confirm msg, i.e. send next step
        if data.get('action') == 'confirm_step':
          step_id = data.get('step_id')
          print(f'Worker confirmed step : {step_id}')
          logger.info(f'Worker confirmed step: {step_id}')

          next_step = 'step_002'
          await websocket.send_text(json.dumps({'action': 'load_step', 'step_id': next_step}))
          logger.info(f"Sent 'load_step' for '{next_step}' to client.")

    except WebSocketDisconnect:
      connected_clients.remove(websocket)
      print(f"Unity client must've disconnected: {len(connected_clients)}")
      logger.info(f"Unity client disconnected. Remaining clients: {len(connected_clients)}")
    except json.JSONDecodeError:
      logger.error(f"Received invalid JSON from client: {raw}")
    except Exception as e:
      logger.exception(f"An unexpected error occurred in websocket: {e}")


  # test func for sending a broadcast to all connected devices.
async def broadcast(message: dict):
    logger.info(f"Broadcasting message to {len(connected_clients)} clients: {message}")
    for client in connected_clients:
      await client.send_text(json.dumps(message))
      try:
        await client.send_text(json.dumps(message))
      except Exception as e:
        logger.error(f"Failed to send message to client {client}: {e}")
