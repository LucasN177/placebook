-- Placebook: Speicherung der Besuche ohne Login.
--
-- Jedes Gerät (bzw. jede Gruppe verbundener Geräte) hat einen zufälligen Sync-Code (UUID). Die App schickt ihn
-- bei jeder Anfrage im Header "x-sync-key" mit. Row Level Security gibt nur Zeilen dieses Codes heraus –
-- ohne Code sieht man nichts, fremde Codes lassen sich nicht auflisten.
--
-- Einmalig im Supabase-Dashboard unter "SQL Editor" ausführen.

create table if not exists public.visits (
    sync_key    uuid        not null,
    category_id text        not null,
    place_id    text        not null,
    visited_at  date,
    note        text        check (char_length(note) <= 500),
    deleted     boolean     not null default false,  -- Löschungen als "Grabstein", damit sie andere Geräte erreichen
    updated_at  timestamptz not null,                -- Zeitpunkt der Änderung laut Gerät, entscheidet Konflikte
    synced_at   timestamptz not null default now(),  -- vom Server gesetzt, die App holt Änderungen danach ab
    primary key (sync_key, category_id, place_id)
);

create index if not exists visits_sync_key_synced_at on public.visits (sync_key, synced_at);

-- Last write wins: ein älterer Stand überschreibt keinen neueren. synced_at setzt immer der Server,
-- damit falsch gehende Geräteuhren beim Abholen keine Änderungen verschlucken.
create or replace function public.visits_before_write() returns trigger
language plpgsql as $$
begin
    if tg_op = 'UPDATE' and new.updated_at < old.updated_at then
        return null;
    end if;
    new.synced_at := now();
    return new;
end;
$$;

drop trigger if exists visits_before_write on public.visits;
create trigger visits_before_write
    before insert or update on public.visits
    for each row execute function public.visits_before_write();

-- Zugriff nur mit passendem Sync-Code im Header
alter table public.visits enable row level security;

drop policy if exists "nur mit eigenem Sync-Code" on public.visits;
create policy "nur mit eigenem Sync-Code" on public.visits
    for all to anon
    using      (sync_key = nullif(current_setting('request.headers', true)::json ->> 'x-sync-key', '')::uuid)
    with check (sync_key = nullif(current_setting('request.headers', true)::json ->> 'x-sync-key', '')::uuid);

-- Hinweis: Supabase vergibt an "anon" standardmäßig alle Rechte auf neue Tabellen, auch DELETE. Die Regel oben
-- beschränkt jede Operation auf Zeilen mit dem eigenen Sync-Code – wer den Code kennt, kann also auch löschen.
-- Die App selbst löscht nie, sondern markiert Besuche als "deleted".
grant select, insert, update on public.visits to anon;
