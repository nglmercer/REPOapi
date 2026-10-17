import { randomUUID } from 'node:crypto';
import { pathToFileURL } from 'node:url';

export class ApiError extends Error {
  constructor(status, response) {
    super(`HTTP ${status} ${response.error?.code ?? 'API_ERROR'}: ${response.error?.message ?? 'Request failed'}`);
    this.status = status;
    this.response = response; // Includes error.details.spawned when a batch partially completes.
  }
}

export class RepoApi {
  constructor({ token = process.env.REPO_API_TOKEN, baseUrl = process.env.REPO_API_URL ?? 'http://127.0.0.1:17453', timeoutMs = 15000 } = {}) {
    if (!token?.trim()) throw new Error('Set REPO_API_TOKEN to AuthenticationToken from your BepInEx config.');
    this.token = token.trim();
    this.baseUrl = baseUrl.replace(/\/+$/, '');
    this.timeoutMs = timeoutMs;
  }

  async request(method, path, body, idempotencyKey) {
    if (!['GET', 'POST'].includes(method) || !path.startsWith('/') || path.startsWith('//')) {
      throw new Error('Use GET or POST with an API path starting with one slash.');
    }
    const headers = { Authorization: `Bearer ${this.token}` };
    let payload;
    if (method === 'POST') {
      idempotencyKey ??= randomUUID();
      headers['Content-Type'] = 'application/json';
      headers['Idempotency-Key'] = idempotencyKey;
      payload = JSON.stringify(body ?? {});
      console.error(`Idempotency-Key: ${idempotencyKey}`);
    }
    let response;
    try {
      response = await fetch(`${this.baseUrl}${path}`, {
        method, headers, body: payload, redirect: 'error',
        signal: AbortSignal.timeout(this.timeoutMs),
      });
    } catch {
      throw new Error(method === 'POST'
        ? `Response not confirmed. Retry the SAME path and body with Idempotency-Key ${idempotencyKey}; do not generate a new key.`
        : 'Cannot reach the API within the client timeout. Check the game and REPO_API_URL.');
    }
    let result;
    try { result = await response.json(); }
    catch { throw new Error(`HTTP ${response.status}: expected JSON. ${method === 'POST' ? 'Retain the printed idempotency key before retrying.' : ''}`); }
    if (!response.ok || result.success === false) throw new ApiError(response.status, result);
    return result;
  }

  get(path) { return this.request('GET', path); }
  post(path, body, key) { return this.request('POST', path, body, key); }
}

// Importing this file does not send requests. CLI usage: node repo-api.mjs GET /health
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    const [method, path, json, key] = process.argv.slice(2);
    if (!method || !path) throw new Error('Usage: node repo-api.mjs GET|POST /path [JSON] [idempotency-key]');
    const result = await new RepoApi().request(method, path, json === undefined ? undefined : JSON.parse(json), key);
    console.log(JSON.stringify(result, null, 2));
  } catch (error) {
    console.error(error.message);
    if (error instanceof ApiError) console.error(JSON.stringify(error.response, null, 2));
    process.exitCode = 1;
  }
}
