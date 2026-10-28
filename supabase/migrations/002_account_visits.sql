-- Placebook: Besuche angemeldeter Nutzer (Supabase Auth, E-Mail + Passwort).
--
-- Eigene Tabelle statt Erweiterung von "visits": Konto- und Sync-Code-Daten bleiben strikt getrennt, damit ein
-- Sync-Code niemals Zugriff auf Kontodaten geben kann. Zugriff nur für angemeldete Nutzer auf eigene Zeilen.
--
-- Einmalig im Supabase-Dashboard unter "SQL Editor" ausführen (nach 001_visits.sql).

create table if not exists public.account_visits (
    user_id     uuid        not null default auth.uid() references auth.users (id) on delete cascade,
    category_id text        not null,
    place_id    text        not null,
    visited_at  date,
    note        text        check (char_length(note) <= 500),
    deleted     boolean     not null default false,
    updated_at  timestamptz not null,
    synced_at   timestamptz not null default now(),
    primary key (user_id, category_id, place_id)
);

create index if not exists account_visits_user_synced_at on public.account_visits (user_id, synced_at);

-- Gleiche Regel wie bei "visits": last write wins, synced_at vom Server (Funktion aus 001_visits.sql)
drop trigger if exists account_visits_before_write on public.account_visits;
create trigger account_visits_before_write
    before insert or update on public.account_visits
    for each row execute function public.visits_before_write();

alter table public.account_visits enable row level security;

drop policy if exists "eigene Besuche" on public.account_visits;
create policy "eigene Besuche" on public.account_visits
    for all to authenticated
    using      (user_id = (select auth.uid()))
    with check (user_id = (select auth.uid()));

-- Nicht angemeldete Zugriffe bekommen keinerlei Rechte auf diese Tabelle
revoke all on public.account_visits from anon;
grant select, insert, update, delete on public.account_visits to authenticated;
