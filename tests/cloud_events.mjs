// Real long-poll connection against the isolated PostgreSQL-backed application.
import { createRequire } from 'node:module';
import { resolve } from 'node:path';
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
const require = createRequire(resolve(process.argv[2], 'package.json'));
const { HubConnectionBuilder, HttpTransportType, LogLevel } = require('@microsoft/signalr');
let input='';for await(const chunk of process.stdin)input+=chunk;
const { origin, cookie, listId }=JSON.parse(input);
const connection=new HubConnectionBuilder().withUrl(origin+'/api/shopping-events',{
 transport:HttpTransportType.LongPolling,headers:{Cookie:cookie,'X-RecipeAtlas':'1'}
}).configureLogging(LogLevel.None).build();
let events=0;connection.on('Changed',()=>events++);
try {
 await connection.start();await connection.invoke('Watch',listId);
 const command={id:randomUUID(),operationId:randomUUID(),text:'Event test'};
 const send=async body=>fetch(origin+'/api/shopping-lists/'+listId+'/items',{method:'POST',headers:{Cookie:cookie,'X-RecipeAtlas':'1','Content-Type':'application/json'},body:JSON.stringify(body)});
 assert.equal((await send(command)).status,200);
 for(let i=0;i<100 && events===0;i++)await new Promise(r=>setTimeout(r,50));
 assert.equal(events,1,'Committed operation emits an invalidation');
 assert.equal((await send(command)).status,200);
 assert.equal((await send({...command,text:'Rejected reuse'})).status,409);
 await new Promise(r=>setTimeout(r,300));
 assert.equal(events,1,'Replay and rejected operations must not emit business invalidations');
 await connection.stop();await connection.start();await connection.invoke('Watch',listId);
 assert.equal((await send({id:randomUUID(),operationId:randomUUID(),text:'After reconnect'})).status,200);
 for(let i=0;i<100 && events<2;i++)await new Promise(r=>setTimeout(r,50));
 assert.equal(events,2,'Reconnected watch receives changes');
 console.log('Passed PostgreSQL post-commit notification, replay suppression and reconnect checks.');
} finally {await connection.stop();}
