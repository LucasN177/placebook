-- Placebook: Angabe „Glühwein getrunken“ bei Besuchen (Kategorie Weihnachtsmärkte).
--
-- null = keine Angabe. Die App liest und schreibt die Spalte bei jeder Synchronisierung – ohne diese Migration
-- schlägt die Synchronisierung fehl.
--
-- Einmalig im Supabase-Dashboard unter "SQL Editor" ausführen (nach 002_account_visits.sql).

alter table public.visits         add column if not exists mulled_wine boolean;
alter table public.account_visits add column if not exists mulled_wine boolean;
