import { RepoApi, ApiError } from './repo-api.mjs';

// Explicitly run this script to spawn content; it is not run by the plugin.
try {
  const [kind = 'item', search = '', quantityText = '1'] = process.argv.slice(2);
  const categories = { item: 'items', enemy: 'enemies', valuable: 'valuables', upgrade: 'upgrades' };
  if (!categories[kind]) throw new Error('Usage: node spawn.mjs item|enemy|valuable|upgrade [search] [quantity]');
  const quantity = Number(quantityText);
  const api = new RepoApi();
  const state = await api.get('/health');
  if (!state.gameplayReady) throw new Error('Host or start singleplayer and enter a generated gameplay level first.');
  const capabilities = await api.get('/api/capabilities');
  if (!capabilities.spawn?.[kind]) throw new Error(`Spawning ${kind} is unavailable in this mod version.`);
  if (!Number.isInteger(quantity) || quantity < 1 || quantity > capabilities.limits.maxSpawnQuantity) {
    throw new Error(`quantity must be an integer from 1 to ${capabilities.limits.maxSpawnQuantity}.`);
  }
  const category = categories[kind];
  const catalog = await api.get(`/api/catalog/${category}?search=${encodeURIComponent(search)}`);
  const entry = catalog[category].find(value => value.spawnable);
  if (!entry) throw new Error(`No matching ${kind}. Change the search or wait for content registration in a level.`);
  console.error(`Selected ${entry.name} (${entry.id})`);
  const body = { [`${kind}Id`]: entry.id, quantity, position: { mode: 'random_player' } };
  console.error(`Request body: ${JSON.stringify(body)}`);
  const result = await api.post(`/api/spawn/${kind}`, body);
  console.log(JSON.stringify(result, null, 2));
} catch (error) {
  console.error(error.message);
  if (error instanceof ApiError) console.error(JSON.stringify(error.response, null, 2));
  process.exitCode = 1;
}
