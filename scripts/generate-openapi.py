#!/usr/bin/env python3
"""Development-only schema generator. The plugin serves the embedded JSON without Python."""
import json
from pathlib import Path
root = Path(__file__).resolve().parents[1]
ref = lambda name: {'$ref': '#/components/schemas/' + name}
obj = lambda properties, required=(): {'type':'object','properties':properties,'additionalProperties':False} | ({'required':list(required)} if required else {})
string = {'type':'string'}
boolean = {'type':'boolean'}
schemas = {}
schemas['Coordinates'] = obj({axis:{'type':'number','format':'float'} for axis in 'xyz'}, 'xyz')
positions = []
for mode in ('near_player','random_player','random_valid','world_position'):
    properties = {'mode': {'type':'string','enum':[mode]}}
    required = ['mode']
    if mode == 'near_player': properties['playerId'] = {'type':'string','minLength':1,'maxLength':256}; required.append('playerId')
    if mode == 'world_position':
        properties.update({axis:{'type':'number','format':'float','minimum':-10000,'maximum':10000} for axis in 'xyz'})
        required += list('xyz')
    positions.append(obj(properties, required))
schemas['Position'] = {'oneOf':positions,'description':'Placement is validated against prefab collider bounds and floor geometry; enemies also require a nearby NavMesh. world_position preserves x/z and projects y onto a nearby floor; it is rejected when occupied. Multiple objects cannot occupy the same explicit world position.'}
schemas['ApiError'] = obj({'success':{'type':'boolean','enum':[False]},'error':obj({'code':string,'message':string,'details':{'nullable':True}},['code','message'])}, ['success','error'])
schemas['Health'] = obj({'status':{'type':'string','enum':['ready']},'gameRunning':boolean,'inLevel':boolean,'isHost':boolean,'gameplayReady':boolean,'apiVersion':string},['status','gameRunning','inLevel','isHost','gameplayReady','apiVersion'])
schemas['Version'] = obj({k:string for k in ['apiVersion','modVersion','gameVersion','compiledGameVersion','bepinexVersion','repoLibVersion']} | {'runtimeCompatibilityVerified':boolean},['apiVersion','modVersion','gameVersion','compiledGameVersion','runtimeCompatibilityVerified'])
schemas['Capabilities'] = {'type':'object','properties':{'apiVersion':string,'requiresHost':boolean,'requiresGeneratedLevel':boolean,'multiplayerTested':boolean,'catalogs':{'type':'array','items':string},'spawn':{'type':'object'},'players':{'type':'object'},'enemies':{'type':'object'},'positions':{'type':'array','items':string},'positioning':string,'idempotency':obj({'required':boolean,'retentionSeconds':{'type':'integer'},'persistentAcrossRestart':boolean},['required','retentionSeconds','persistentAcrossRestart']),'limits':{'type':'object'},'directUpgradeGrant':{'type':'object'},'gameThread':boolean,'reflection':boolean,'harmonyPatches':boolean},'required':['apiVersion','multiplayerTested','spawn','players','idempotency']}
schemas['CatalogEntry'] = obj({'id':string,'name':string,'category':{'type':'string','enum':['item','enemy','valuable','upgrade']},'spawnable':boolean,'entitiesPerSpawn':{'type':'integer','minimum':1}},['id','name','category','spawnable','entitiesPerSpawn'])
schemas['Player'] = obj({'id':{'type':'string','description':'Opaque ID, valid only while this player remains in this scene/session.'},'name':string,'position':ref('Coordinates')},['id','name','position'])
schemas['Enemy'] = obj({'id':string,'name':string,'apiSpawned':boolean,'position':ref('Coordinates')},['id','name','apiSpawned','position'])
schemas['SpawnedObject'] = obj({'id':string,'catalogId':string,'position':ref('Coordinates')},['catalogId','position'])
schemas['SpawnResult'] = obj({'success':{'type':'boolean','enum':[True]},'result':obj({'spawned':{'type':'array','items':ref('SpawnedObject')},'count':{'type':'integer'},'replicationVerified':boolean},['spawned','count','replicationVerified'])},['success','result'])
schemas['PlayerResult'] = obj({'success':{'type':'boolean','enum':[True]},'result':obj({'playerId':string,'action':{'type':'string','enum':['heal','revive']},'outcome':{'type':'string','enum':['invoked']},'effectConfirmed':{'type':'boolean','enum':[False]}},['playerId','action','outcome','effectConfirmed'])},['success','result'])
schemas['DespawnResult'] = obj({'success':{'type':'boolean','enum':[True]},'result':obj({'removed':{'type':'array','items':string},'outcome':{'type':'string','enum':['destroy_requested']},'replicationVerified':boolean},['removed','outcome','replicationVerified'])},['success','result'])
paths = {}
def operation(path, method, summary, response, description='', body=None, parameters=()):
    responses = {'200':{'description':'Completed on the game thread. Mutation results state the confirmed scope of execution.','content':{'application/json':{'schema':response}}}}
    descriptions = {'400':'Invalid JSON, quantity, position, unknown field, query or missing idempotency key.','401':'Missing/invalid bearer token.','404':'Unknown route, object, player or API-owned enemy.','405':'Unsupported HTTP method.','409':'No active level, host required, changed session, lost ownership or reused idempotency key with different body.','413':'Payload exceeds configured byte limit.','415':'POST requires application/json.','422':'No safe position or usable prefab collision bounds.','429':'Global request rate exceeded.','500':'Execution failed; details may contain confirmed partial spawn results.','501':'Operation is unavailable; inspect capabilities.','503':'Shutdown, full command queue, HTTP or idempotency capacity.','504':'Command expired before executing. No action performed.'}
    for code, message in descriptions.items(): responses[code] = {'description':message,'content':{'application/json':{'schema':ref('ApiError')}}}
    value = {'summary':summary,'description':description,'operationId':method + '_' + path.strip('/').replace('/','_').replace('{','').replace('}',''),'responses':responses}
    params = list(parameters)
    if body is not None:
        value['requestBody'] = {'required':True,'content':{'application/json':{'schema':body}}}
        params.append({'in':'header','name':'Idempotency-Key','required':True,'schema':{'type':'string','minLength':1,'maxLength':128},'description':'Use a new unique key for a new command. Reuse the same key and body on retries; retained in memory for at least 10 minutes. It does not survive game restart.'})
    if params: value['parameters'] = params
    paths[path] = {method:value}
operation('/health','get','HTTP and gameplay readiness',ref('Health'),'HTTP readiness remains ready before any level is loaded. game readiness requires a generated playable level and host authority. This request also uses the Unity dispatcher; a stalled Unity loop returns 504.')
operation('/api/game/state','get','Current game readiness',ref('Health'))
operation('/api/version','get','Mod, game and dependency versions',ref('Version'))
operation('/api/capabilities','get','Supported actions and limits',ref('Capabilities'),'Multiplayer is unverified. Player actions report invocation, not effect acknowledgement. Teleport and direct upgrade grants are unavailable.')
operation('/openapi.json','get','OpenAPI specification',{'type':'object'})
for category in ['items','enemies','valuables','upgrades']:
    operation('/api/catalog/' + category,'get','List registered ' + category,obj({category:{'type':'array','items':ref('CatalogEntry')}},[category]),'Uses REPOLib collections, validates prefab references and root PhotonViews, includes registered mod content, rejects ambiguous IDs. Catalog may be empty before registration. Upgrade catalog contains physical pickups. Cached for up to 3 seconds.',parameters=[{'in':'query','name':'search','schema':string,'description':'Case-insensitive substring match in name or ID.'},{'in':'query','name':'category','schema':{'type':'string','enum':[category]},'description':'Optional explicit category filter; must match the endpoint.'}])
operation('/api/players','get','List current players',obj({'players':{'type':'array','items':ref('Player')}},['players']))
operation('/api/players/{id}','get','Get a current player',ref('Player'),parameters=[{'in':'path','name':'id','required':True,'schema':string}])
operation('/api/enemies','get','List active scene enemies',obj({'enemies':{'type':'array','items':ref('Enemy')}},['enemies']),'Includes native enemies with apiSpawned=false. Native IDs cannot be used to delete them.')
for category in ['item','enemy','valuable','upgrade']:
    name = category + 'Id'
    body = obj({name:{'type':'string','minLength':1,'maxLength':256},'quantity':{'type':'integer','minimum':1,'maximum':100,'default':1,'description':'Limited by configured MaxSpawnQuantity (default 10).'},'position':ref('Position')},[name,'position'])
    operation('/api/spawn/' + category,'post','Spawn ' + category,ref('SpawnResult'),'Host/singleplayer and generated gameplay level only. ID must come from the current catalog. All positions are checked before spawning; runtime failure can produce partial results in error.details.spawned. Success confirms returned objects, never remote client replication. Upgrades spawn physical pickups and are not granted directly.',body)
for action in ['heal','revive','teleport']:
    props = {'playerId':{'type':'string','minLength':1,'maxLength':256}}
    if action == 'heal': props['amount'] = {'type':'integer','minimum':1,'maximum':1000}
    if action == 'teleport': props['position'] = ref('Position')
    operation('/api/player/' + action,'post',action.capitalize() + ' a player',ref('PlayerResult'),'Host and generated gameplay level required. ' + ('Currently unavailable: returns 501 UNSUPPORTED_ACTION.' if action == 'teleport' else 'Calls the public game method and returns outcome=invoked, effectConfirmed=false. No owner acknowledgement is available; game prerequisites can cause a no-op.'),obj(props,list(props)))
operation('/api/enemies/clear','post','Remove all current API-spawned enemies',ref('DespawnResult'),'Host only. Does not delete native level enemies; tracking is cleared on scene/session/authority changes. Network destroy is requested; remote removal is not acknowledged.',obj({}))
operation('/api/enemies/despawn','post','Remove one API-spawned enemy',ref('DespawnResult'),'Host only; ID must identify an entity created by this API in the current scene. Native enemies cannot be removed.',obj({'enemyId':{'type':'string','minLength':1,'maxLength':256}},['enemyId']))
doc = {'openapi':'3.0.3','info':{'title':'R.E.P.O. REST API','version':'1.0.0','description':'Standalone BepInEx 5 plugin with REPOLib 4.2.0. No TikTools runtime dependency. All endpoints require the configured bearer token by default. Gameplay mutations require host authority and a generated playable level. Runtime and multiplayer validation status is in docs/TESTING.md.'},'servers':[{'url':'http://127.0.0.1:17453'}],'security':[{'bearerAuth':[]}],'paths':paths,'components':{'securitySchemes':{'bearerAuth':{'type':'http','scheme':'bearer'}},'schemas':schemas}}
(root/'docs/openapi.json').write_text(json.dumps(doc, indent=2)+'\n')
