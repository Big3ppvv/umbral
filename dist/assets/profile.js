const STORAGE_KEY = "umbra.profile.v1";
const MAX_LEVEL = 5;
const MAX_COUNTER = 1_000_000_000;
const EMPTY_PROFILE = {
    version: 1,
    shards: 0,
    vitality: 0,
    power: 0,
    runs: 0,
    wins: 0,
    bestRoom: 0,
    bestTime: null,
};
function object(value) {
    return typeof value === "object" && value !== null && !Array.isArray(value);
}
function integer(value, min, max) {
    return typeof value === "number" && Number.isSafeInteger(value) && value >= min && value <= max;
}
function validProfile(value) {
    return object(value)
        && value.version === 1
        && integer(value.shards, 0, MAX_COUNTER)
        && integer(value.vitality, 0, MAX_LEVEL)
        && integer(value.power, 0, MAX_LEVEL)
        && integer(value.runs, 0, MAX_COUNTER)
        && integer(value.wins, 0, MAX_COUNTER)
        && value.wins <= value.runs
        && integer(value.bestRoom, 0, 6)
        && (value.bestTime === null
            || (typeof value.bestTime === "number" && Number.isFinite(value.bestTime)
                && value.bestTime >= 0 && value.bestTime <= 86_400));
}
function copyProfile(value) {
    return {
        version: 1,
        shards: value.shards,
        vitality: value.vitality,
        power: value.power,
        runs: value.runs,
        wins: value.wins,
        bestRoom: value.bestRoom,
        bestTime: value.bestTime,
    };
}
function validRun(run) {
    return typeof run.id === "string" && /^[a-zA-Z0-9_-]{8,80}$/.test(run.id)
        && typeof run.won === "boolean"
        && integer(run.room, 1, 6)
        && (!run.won || run.room === 6)
        && integer(run.kills, 0, 100_000)
        && typeof run.duration === "number" && Number.isFinite(run.duration)
        && run.duration >= 0 && run.duration <= 86_400
        && integer(run.shards, 0, 1_000);
}
export class ProfileStore {
    profile = { ...EMPTY_PROFILE };
    mode = "memory";
    completedRuns = new Set();
    initialized = false;
    pending = Promise.resolve();
    async init() {
        if (this.initialized)
            return this.profile;
        this.readLocal();
        try {
            const profile = await this.request("/api/profile", undefined, 1_500);
            this.profile = profile;
            this.mode = "server";
        }
        catch {
            // A static deployment has no Python API; the browser save remains usable.
        }
        this.initialized = true;
        return this.profile;
    }
    async recordRun(run) {
        if (!validRun(run))
            throw new Error("La partida contiene datos inválidos y no se pudo guardar.");
        return this.serial(async () => {
            if (!this.initialized)
                await this.init();
            if (this.mode === "server") {
                this.profile = await this.request("/api/run", run);
                return this.profile;
            }
            if (this.mode === "local")
                this.readLocal();
            if (this.completedRuns.has(run.id))
                return this.profile;
            const p = this.profile;
            this.profile = {
                ...p,
                shards: Math.min(MAX_COUNTER, p.shards + run.shards),
                runs: Math.min(MAX_COUNTER, p.runs + 1),
                wins: Math.min(MAX_COUNTER, p.wins + (run.won ? 1 : 0)),
                bestRoom: Math.max(p.bestRoom, run.room),
                bestTime: run.won ? Math.min(p.bestTime ?? run.duration, run.duration) : p.bestTime,
            };
            this.completedRuns.add(run.id);
            this.writeLocal();
            return this.profile;
        });
    }
    async upgrade(kind) {
        if (kind !== "vitality" && kind !== "power")
            throw new Error("Esa mejora no existe.");
        return this.serial(async () => {
            if (!this.initialized)
                await this.init();
            if (this.mode === "server") {
                this.profile = await this.request("/api/upgrade", { kind });
                return this.profile;
            }
            if (this.mode === "local")
                this.readLocal();
            const level = this.profile[kind];
            if (level >= MAX_LEVEL)
                throw new Error("Esta mejora ya está al nivel máximo.");
            const cost = 15 * (level + 1);
            if (this.profile.shards < cost)
                throw new Error(`Necesitas ${cost} fragmentos para esta mejora.`);
            this.profile = { ...this.profile, shards: this.profile.shards - cost, [kind]: level + 1 };
            this.writeLocal();
            return this.profile;
        });
    }
    serial(operation) {
        const next = this.pending.then(operation, operation);
        this.pending = next.catch(() => undefined);
        return next;
    }
    readLocal() {
        try {
            const saved = localStorage.getItem(STORAGE_KEY);
            // Test write access even when there is no saved profile or storage is read-only.
            const probe = `${STORAGE_KEY}.probe`;
            localStorage.setItem(probe, "1");
            localStorage.removeItem(probe);
            this.mode = "local";
            if (!saved)
                return;
            const parsed = JSON.parse(saved);
            if (!object(parsed) || parsed.version !== 1 || !validProfile(parsed.profile)
                || !Array.isArray(parsed.completedRuns)
                || !parsed.completedRuns.every((id) => typeof id === "string"
                    && /^[a-zA-Z0-9_-]{8,80}$/.test(id)))
                return;
            this.profile = copyProfile(parsed.profile);
            this.completedRuns = new Set(parsed.completedRuns);
        }
        catch {
            // Corrupted JSON still allows a fresh local save. Access failures use memory.
            try {
                const probe = `${STORAGE_KEY}.probe`;
                localStorage.setItem(probe, "1");
                localStorage.removeItem(probe);
                this.mode = "local";
            }
            catch {
                this.mode = "memory";
            }
        }
    }
    writeLocal() {
        if (this.mode === "memory")
            return;
        const saved = {
            version: 1,
            profile: this.profile,
            completedRuns: [...this.completedRuns],
        };
        try {
            localStorage.setItem(STORAGE_KEY, JSON.stringify(saved));
        }
        catch {
            this.mode = "memory";
        }
    }
    async request(path, body, timeout = 5_000) {
        const controller = new AbortController();
        const timer = setTimeout(() => controller.abort(), timeout);
        try {
            const response = await fetch(path, {
                method: body === undefined ? "GET" : "POST",
                headers: { Accept: "application/json", ...(body === undefined ? {} : { "Content-Type": "application/json" }) },
                body: body === undefined ? undefined : JSON.stringify(body),
                signal: controller.signal,
                credentials: "same-origin",
                cache: "no-store",
            });
            let result;
            try {
                result = await response.json();
            }
            catch {
                throw new Error("El servidor no devolvió una respuesta válida. Vuelve a intentar guardar.");
            }
            if (!response.ok) {
                throw new Error(object(result) && typeof result.error === "string"
                    ? result.error : "No se pudo guardar el progreso. Vuelve a intentarlo.");
            }
            if (!validProfile(result))
                throw new Error("El perfil recibido no es válido. Vuelve a intentarlo.");
            return copyProfile(result);
        }
        catch (error) {
            if (controller.signal.aborted) {
                throw new Error(path === "/api/upgrade"
                    ? "El servidor tardó demasiado. Recarga el perfil antes de volver a comprar para comprobar el resultado."
                    : "El servidor tardó demasiado. Reintenta guardar la misma partida para comprobar el resultado.");
            }
            if (error instanceof TypeError)
                throw new Error("Sin conexión con el servidor. Reintenta guardar cuando vuelva la conexión.");
            throw error;
        }
        finally {
            clearTimeout(timer);
        }
    }
}
