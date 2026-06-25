import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError, fetchApi } from '../src/lib/api';

describe('fetchApi', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('should parse JSON successfully on 200 response', async () => {
    const mockData = { id: 1, name: 'Test' };
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: () => Promise.resolve(mockData),
      text: () => Promise.resolve(JSON.stringify(mockData)),
    } as unknown as Response);

    const result = await fetchApi('/test');
    expect(result).toEqual(mockData);

    expect(fetch).toHaveBeenCalledTimes(1);
    const fetchCallUrl = vi.mocked(fetch).mock.calls[0][0];
    expect(fetchCallUrl).toContain('/test');
  });

  it('should return null for 204 No Content', async () => {
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: true,
      status: 204,
      json: () => Promise.reject(new Error('Should not be called')),
    } as unknown as Response);

    const result = await fetchApi('/test-204');
    expect(result).toBeNull();
  });

  it('should return null for 205 Reset Content', async () => {
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: true,
      status: 205,
      json: () => Promise.reject(new Error('Should not be called')),
    } as unknown as Response);

    const result = await fetchApi('/test-205');
    expect(result).toBeNull();
  });

  it('should not set Content-Type header when body is FormData', async () => {
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: true,
      status: 200,
      text: () => Promise.resolve('{"success": true}'),
    } as unknown as Response);

    const formData = new FormData();
    formData.append('file', 'test');

    await fetchApi('/upload', { method: 'POST', body: formData });

    const fetchCallArgs = vi.mocked(fetch).mock.calls[0][1];
    expect(fetchCallArgs?.headers).toBeDefined();
    const headers = fetchCallArgs?.headers as Headers;
    expect(headers.has('Content-Type')).toBe(false);
  });

  it('should normalize Content-Type casing before deciding request headers', async () => {
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: true,
      status: 200,
      text: () => Promise.resolve('{"success": true}'),
    } as unknown as Response);

    const formData = new FormData();
    formData.append('file', 'test');

    await fetchApi('/upload', {
      method: 'POST',
      body: formData,
      headers: { 'content-type': 'application/json' },
    });

    const fetchCallArgs = vi.mocked(fetch).mock.calls[0][1];
    const headers = fetchCallArgs?.headers as Headers;
    expect(headers.has('Content-Type')).toBe(false);
  });

  it('should throw ApiError on non-2xx responses', async () => {
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: false,
      status: 400,
      statusText: 'Bad Request',
      json: () => Promise.resolve({ error: 'Validation failed' }),
    } as unknown as Response);

    let thrownError: unknown;
    try {
      await fetchApi('/fail');
    } catch (e) {
      thrownError = e;
    }

    expect(thrownError).toBeInstanceOf(ApiError);
    if (thrownError instanceof ApiError) {
      expect(thrownError.status).toBe(400);
      expect(thrownError.data).toEqual({ error: 'Validation failed' });
    }
  });

  it('should timeout after configured time', async () => {
    // Cannot reliably use fake timers with fetch inside Vitest.
    // Simulate AbortError by listening to signal.abort event.
    vi.mocked(fetch).mockImplementationOnce(async (_url, init) => {
      return new Promise((_, reject) => {
        if (init?.signal) {
          init.signal.addEventListener('abort', () => {
            const err = new Error('The operation was aborted');
            err.name = 'AbortError';
            reject(err);
          });
        }
      });
    });

    let thrownError: unknown;
    try {
      await fetchApi('/timeout', { timeout: 10 });
    } catch (e) {
      thrownError = e;
    }

    expect(thrownError).toBeInstanceOf(Error);
    if (thrownError instanceof Error) {
      expect(thrownError.message).toBe('The operation was aborted');
    }
  });
});
