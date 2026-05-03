import asyncio, struct, json, websockets

async def main():
    async with websockets.connect('ws://127.0.0.1:8765', subprotocols=['foxglove.sdk.v1']) as ws:
        for _ in range(2):
            await ws.recv()  # drain serverInfo + advertise

        # Subscribe to /tf
        sub = '{"op":"subscribe","subscriptions":[{"id":100,"channelId":2147483650}]}'
        await ws.send(sub)

        for i in range(20):
            try:
                msg = await asyncio.wait_for(ws.recv(), timeout=3)
                if isinstance(msg, bytes) and len(msg) >= 13 and msg[0] == 1:
                    sub_id = struct.unpack('<I', msg[1:5])[0]
                    log_ns = struct.unpack('<Q', msg[5:13])[0]
                    payload = msg[13:]
                    data = json.loads(payload)
                    child = data.get('child_frame_id','?')
                    tx = data.get('translation',{}).get('x',0)
                    print(f'Msg #{i}: subId={sub_id} logTime={log_ns} child={child} tx={tx}')
                elif isinstance(msg, bytes) and msg[0] == 2:
                    t = struct.unpack('<Q', msg[1:9])[0]
                    print(f'Time: {t}')
            except asyncio.TimeoutError:
                print('timeout')
                break

asyncio.run(main())
