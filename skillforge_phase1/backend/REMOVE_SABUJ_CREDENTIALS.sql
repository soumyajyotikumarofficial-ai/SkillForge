-- SkillForge: Remove "sabuj" Login Credentials
-- =============================================
-- 
-- WARNING: This script permanently removes all "sabuj" user accounts and associated data.
-- Run this script ONLY if you're certain you want to delete these credentials.
-- 
-- BEFORE RUNNING:
-- 1. Backup your database: 
--    SQLite: Copy skillforge.db to skillforge.db.backup
-- 2. Review the DELETE statements below
-- 3. Uncomment the DELETE statements to execute
--
-- After deletion, all candidates, jobs, and hiring workflows associated with "sabuj" 
-- user accounts will be permanently removed. This CANNOT be undone.

-- ============================================================================
-- STEP 1: Identify "sabuj" Accounts
-- ============================================================================
-- 
-- First, let's see what "sabuj" accounts exist:

SELECT 'Users with sabuj username:' as Info;
SELECT UserId, Username, Email, Role FROM Users WHERE Username LIKE '%sabuj%';

SELECT 'Recruiters associated with sabuj:' as Info;
SELECT r.Id, r.Name, r.Email, u.Username FROM Recruiters r 
JOIN Users u ON r.UserId = u.UserId 
WHERE u.Username LIKE '%sabuj%';

-- ============================================================================
-- STEP 2: Count Associated Data
-- ============================================================================
-- 
-- See how much data will be affected:

SELECT 'Candidates owned by sabuj users:' as Info;
SELECT COUNT(*) as CandidateCount FROM Candidates c
WHERE c.UserId IN (SELECT UserId FROM Users WHERE Username LIKE '%sabuj%');

SELECT 'Job requests by sabuj recruiters:' as Info;
SELECT COUNT(*) as JobRequestCount FROM CompanyJobRequests cjr
WHERE cjr.RecruiterId IN (SELECT Id FROM Recruiters r 
  WHERE r.UserId IN (SELECT UserId FROM Users WHERE Username LIKE '%sabuj%'));

SELECT 'Project hiring requests by sabuj recruiters:' as Info;
SELECT COUNT(*) as ProjectCount FROM ProjectHiringRequests phr
WHERE phr.RecruiterId IN (SELECT Id FROM Recruiters r 
  WHERE r.UserId IN (SELECT UserId FROM Users WHERE Username LIKE '%sabuj%'));

SELECT 'Email audit logs from sabuj recruiters:' as Info;
SELECT COUNT(*) as EmailCount FROM EmailAuditLogs eal
WHERE eal.RecruiterId IN (SELECT Id FROM Recruiters r 
  WHERE r.UserId IN (SELECT UserId FROM Users WHERE Username LIKE '%sabuj%'));

-- ============================================================================
-- STEP 3: PERMANENT DELETION (UNCOMMENT TO EXECUTE)
-- ============================================================================
-- 
-- WARNING: These operations are IRREVERSIBLE. Uncomment only if you're certain.

-- DELETE FROM EmailAuditLogs
-- WHERE RecruiterId IN (
--   SELECT Id FROM Recruiters r 
--   WHERE r.UserId IN (SELECT UserId FROM Users WHERE Username LIKE '%sabuj%')
-- );

-- DELETE FROM CandidateShortlists
-- WHERE (CompanyJobRequestId IN (
--   SELECT Id FROM CompanyJobRequests 
--   WHERE RecruiterId IN (
--     SELECT Id FROM Recruiters WHERE UserId IN (
--       SELECT UserId FROM Users WHERE Username LIKE '%sabuj%'
--     )
--   )
-- ) OR ProjectHiringRequestId IN (
--   SELECT Id FROM ProjectHiringRequests 
--   WHERE RecruiterId IN (
--     SELECT Id FROM Recruiters WHERE UserId IN (
--       SELECT UserId FROM Users WHERE Username LIKE '%sabuj%'
--     )
--   )
-- ));

-- DELETE FROM CompanyJobRequests
-- WHERE RecruiterId IN (
--   SELECT Id FROM Recruiters WHERE UserId IN (
--     SELECT UserId FROM Users WHERE Username LIKE '%sabuj%'
--   )
-- );

-- DELETE FROM ProjectHiringRequests
-- WHERE RecruiterId IN (
--   SELECT Id FROM Recruiters WHERE UserId IN (
--     SELECT UserId FROM Users WHERE Username LIKE '%sabuj%'
--   )
-- );

-- DELETE FROM Recruiters
-- WHERE UserId IN (SELECT UserId FROM Users WHERE Username LIKE '%sabuj%');

-- DELETE FROM CandidateSkills
-- WHERE CandidateId IN (
--   SELECT CandidateId FROM Candidates 
--   WHERE UserId IN (SELECT UserId FROM Users WHERE Username LIKE '%sabuj%')
-- );

-- DELETE FROM CandidateResumes
-- WHERE CandidateId IN (
--   SELECT CandidateId FROM Candidates 
--   WHERE UserId IN (SELECT UserId FROM Users WHERE Username LIKE '%sabuj%')
-- );

-- DELETE FROM JobMatches
-- WHERE CandidateId IN (
--   SELECT CandidateId FROM Candidates 
--   WHERE UserId IN (SELECT UserId FROM Users WHERE Username LIKE '%sabuj%')
-- );

-- DELETE FROM Candidates
-- WHERE UserId IN (SELECT UserId FROM Users WHERE Username LIKE '%sabuj%');

-- DELETE FROM Users WHERE Username LIKE '%sabuj%';

-- ============================================================================
-- STEP 4: VERIFY DELETION
-- ============================================================================
-- 
-- After uncommenting and running the DELETE statements above, 
-- run these queries to verify all sabuj data is removed:

SELECT 'Verify: Remaining Users with sabuj username (should be 0):' as Info;
SELECT COUNT(*) as RemainingCount FROM Users WHERE Username LIKE '%sabuj%';

SELECT 'Verify: Remaining Recruiters with sabuj affiliation (should be 0):' as Info;
SELECT COUNT(*) as RemainingCount FROM Recruiters r 
WHERE r.UserId IN (SELECT UserId FROM Users WHERE Username LIKE '%sabuj%');

SELECT 'Verify: Remaining Candidates with sabuj affiliation (should be 0):' as Info;
SELECT COUNT(*) as RemainingCount FROM Candidates c
WHERE c.UserId IN (SELECT UserId FROM Users WHERE Username LIKE '%sabuj%');

-- ============================================================================
-- STEP 5: ALTERNATIVE - SOFT DELETE (DISABLE WITHOUT REMOVING)
-- ============================================================================
--
-- If you want to keep data for audit purposes but disable the accounts:
-- Option A: Add IsActive column to Users table (requires migration)
-- Option B: Update PasswordHash to invalid value and disable login

-- UPDATE Users SET PasswordHash = NULL WHERE Username LIKE '%sabuj%';
-- -- Note: This will prevent login but keeps audit trail intact

-- ============================================================================
-- CLEANUP TIPS
-- ============================================================================
--
-- 1. After deleting sabuj users, you may want to delete orphaned files:
--    - Check Uploads/Resumes/ directory
--    - Remove files associated with deleted candidates
--
-- 2. Update any external integrations that referenced sabuj accounts
--
-- 3. Clear application cache/session storage
--
-- 4. Run database integrity checks:
--    PRAGMA integrity_check;  (SQLite)
--    PRAGMA foreign_keys=ON;  (Ensure FK constraints are enabled)
--
-- 5. Document this cleanup action in your audit log/changelog
--
-- ============================================================================
