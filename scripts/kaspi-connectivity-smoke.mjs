const token = process.env.KASPI_TOKEN?.trim();

if (!token) {
  console.error("KASPI_TOKEN is required. Set it only in the current terminal session.");
  process.exit(2);
}

const now = Date.now();
const params = new URLSearchParams({
  "page[number]": "0",
  "page[size]": "1",
  "filter[orders][creationDate][$ge]": String(now - 60 * 60 * 1000),
  "filter[orders][creationDate][$le]": String(now),
});

const controller = new AbortController();
const timeout = setTimeout(() => controller.abort(), 15_000);

try {
  const response = await fetch(`https://kaspi.kz/shop/api/v2/orders?${params}`, {
    headers: {
      Accept: "application/vnd.api+json",
      "X-Auth-Token": token,
    },
    redirect: "error",
    signal: controller.signal,
  });

  const result = {
    reachable: true,
    authenticated: response.ok,
    status: response.status,
    statusText: response.statusText,
  };

  if (response.status === 401) result.reason = "TOKEN_UNAUTHORIZED";
  else if (response.status === 403) result.reason = "TOKEN_FORBIDDEN";
  else if (response.status === 429) result.reason = "RATE_LIMITED";
  else if (response.status >= 500) result.reason = "KASPI_UNAVAILABLE";
  else if (!response.ok) result.reason = "KASPI_REQUEST_FAILED";

  console.log(JSON.stringify(result));
  process.exitCode = response.ok ? 0 : 1;
} catch (error) {
  const timeoutError = error instanceof Error && error.name === "AbortError";
  console.error(JSON.stringify({
    reachable: false,
    authenticated: false,
    reason: timeoutError ? "KASPI_TIMEOUT" : "NETWORK_ERROR",
  }));
  process.exitCode = 1;
} finally {
  clearTimeout(timeout);
}
