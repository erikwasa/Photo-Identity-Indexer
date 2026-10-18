-- WI-0202: read-only storage/retention/index investigation for PostgreSQL 18.
-- Does not inspect face/person/photo identifiers, original bytes, SQL text or credentials.
-- Run only against the explicitly verified application database.
-- Safe to repeat against an isolated restored database for comparison.
\set ON_ERROR_STOP on
\pset pager off
\echo 'WI-0202: confirmed connection and database size'
BEGIN READ ONLY;
SET LOCAL statement_timeout = '15s';

SELECT current_database() AS connected_database,
       current_setting('server_version') AS postgres_version,
       pg_database_size(current_database()) AS database_bytes,
       pg_size_pretty(pg_database_size(current_database())) AS database_size;

\echo 'WI-0202: physical table sizes and approximate live/dead rows (not exact row counts)'
SELECT c.relname AS table_name,
       pg_total_relation_size(c.oid) AS total_bytes,
       pg_relation_size(c.oid) AS heap_bytes,
       pg_indexes_size(c.oid) AS index_bytes,
       pg_size_pretty(pg_total_relation_size(c.oid)) AS total_size,
       c.reltuples::bigint AS planner_estimated_rows,
       s.n_live_tup AS monitored_estimated_live,
       s.n_dead_tup AS monitored_estimated_dead,
       s.n_tup_ins,
       s.n_tup_upd,
       s.n_tup_del,
       s.last_analyze,
       s.last_autoanalyze,
       s.last_vacuum,
       s.last_autovacuum
FROM pg_class AS c
JOIN pg_namespace AS n ON n.oid = c.relnamespace
LEFT JOIN pg_stat_user_tables AS s ON s.relid = c.oid
WHERE n.nspname = 'public'
  AND c.relkind IN ('r', 'p')
  AND c.relname IN (
      'provisional_face_cluster_members',
      'provisional_face_cluster_runs',
      'provisional_face_cluster_current',
      'identity_match_regeneration_targets',
      'identity_match_regeneration_runs',
      'review_actions',
      'face_occurrences',
      'assets',
      'asset_revisions')
ORDER BY pg_total_relation_size(c.oid) DESC;

\echo 'WI-0202: individual index sizes and cumulative index scan counts'
-- idx_scan is a since-statistics-reset counter, NOT evidence an index is safe to drop.
SELECT t.relname AS table_name,
       i.relname AS index_name,
       pg_relation_size(i.oid) AS index_bytes,
       pg_size_pretty(pg_relation_size(i.oid)) AS index_size,
       ix.indisprimary AS is_primary,
       ix.indisunique AS is_unique,
       COALESCE(si.idx_scan, 0) AS observed_index_scans
FROM pg_index AS ix
JOIN pg_class AS t ON t.oid = ix.indrelid
JOIN pg_namespace AS n ON n.oid = t.relnamespace
JOIN pg_class AS i ON i.oid = ix.indexrelid
LEFT JOIN pg_stat_user_indexes AS si ON si.indexrelid = ix.indexrelid
WHERE n.nspname = 'public'
  AND t.relname IN (
      'provisional_face_cluster_members',
      'identity_match_regeneration_targets')
ORDER BY pg_relation_size(i.oid) DESC
LIMIT 30;

\echo 'WI-0202: provisional cluster run statuses (aggregate run metadata, not member scans)'
SELECT status,
       COUNT(*) AS runs,
       COALESCE(SUM(target_count), 0)::bigint AS sum_declared_targets,
       COALESCE(SUM(processed_target_count), 0)::bigint AS sum_processed_targets,
       MIN(requested_at_utc)::date AS oldest_run_day,
       MAX(requested_at_utc)::date AS newest_run_day
FROM public.provisional_face_cluster_runs
GROUP BY status
ORDER BY status;

\echo 'WI-0202: current pinned provisional scopes by run status'
SELECT COALESCE(r.status, 'missing-run') AS status,
       COUNT(*) AS current_scope_count,
       COALESCE(SUM(r.target_count), 0)::bigint AS current_scope_declared_targets
FROM public.provisional_face_cluster_current AS current
LEFT JOIN public.provisional_face_cluster_runs AS r ON r.id = current.run_id
GROUP BY COALESCE(r.status, 'missing-run')
ORDER BY status;

\echo 'WI-0202: identity regeneration run statuses (aggregate run metadata)'
SELECT status,
       COUNT(*) AS runs,
       COALESCE(SUM(target_count), 0)::bigint AS sum_declared_targets,
       COALESCE(SUM(processed_target_count), 0)::bigint AS sum_processed_targets,
       MIN(requested_at_utc)::date AS oldest_run_day,
       MAX(requested_at_utc)::date AS newest_run_day
FROM public.identity_match_regeneration_runs
GROUP BY status
ORDER BY status;

\echo 'WI-0202: optional measurement extensions already installed (no extension changes)'
SELECT extname
FROM pg_extension
WHERE extname IN ('pgstattuple', 'pg_stat_statements')
ORDER BY extname;

COMMIT;
