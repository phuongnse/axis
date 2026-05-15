import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { fetchApi, ApiError } from '../src/lib/api';

describe('fetchApi', () => {
  const mockBaseUrl = 'http://localhost/api';

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
    } as any);

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
    } as any);

    const result = await fetchApi('/test-204');
    expect(result).toBeNull();
  });

  it('should return null for 205 Reset Content', async () => {
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: true,
      status: 205,
      json: () => Promise.reject(new Error('Should not be called')),
    } as any);

    const result = await fetchApi('/test-205');
    expect(result).toBeNull();
  });

  it('should not set Content-Type header when body is FormData', async () => {
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: true,
      status: 200,
      text: () => Promise.resolve('{"success": true}'),
    } as any);

    const formData = new FormData();
    formData.append('file', 'test');

    await fetchApi('/upload', { method: 'POST', body: formData });

    const fetchCallArgs = vi.mocked(fetch).mock.calls[0][1];
    expect(fetchCallArgs?.headers).toBeDefined();
    const headers = fetchCallArgs?.headers as Record<string, string>;
    expect(headers['Content-Type']).toBeUndefined(); // It should be removed so browser can set multipart boundary
  });

  it('should throw ApiError on non-2xx responses', async () => {
    vi.mocked(fetch).mockResolvedValueOnce({
      ok: false,
      status: 400,
      statusText: 'Bad Request',
      json: () => Promise.resolve({ error: 'Validation failed' }),
    } as any);

    let error: any;
    try {
      await fetchApi('/fail');
    } catch (e) {
      error = e;
    }

    expect(error).toBeInstanceOf(ApiError);
    expect(error.status).toBe(400);
    expect(error.data).toEqual({ error: 'Validation failed' });
  });

  it('should timeout after configured time', async () => {
    // We cannot reliably use fake timers with fetch inside Vitest
    // Instead we can simulate the AbortError throw directly if signal is aborted
    vi.mocked(fetch).mockImplementationOnce(async (url, init) => {
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

    let error: any;
    try {
      await fetchApi('/timeout', { timeout: 10 });
    } catch (e) {
      error = e;
    }

    expect(error).toBeDefined();
    expect(error.message).toBe('The operation was aborted');
  });
});