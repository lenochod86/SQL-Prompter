-- Test databases used to verify completion in SSMS. Run with the test-database skill (GO on its own line).
IF DB_ID('SqlPrompterTest_Verify') IS NOT NULL BEGIN ALTER DATABASE SqlPrompterTest_Verify SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE SqlPrompterTest_Verify END
IF DB_ID('SqlPrompterTest_Other') IS NOT NULL BEGIN ALTER DATABASE SqlPrompterTest_Other SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE SqlPrompterTest_Other END
CREATE DATABASE SqlPrompterTest_Verify
GO
CREATE DATABASE SqlPrompterTest_Other
GO
-- then, in SqlPrompterTest_Verify:
-- CREATE TABLE dbo.Users (IdUser int NOT NULL PRIMARY KEY, Email nvarchar(100), Name nvarchar(50))
-- CREATE TABLE dbo.OrderItems (IdOrderItem int NOT NULL PRIMARY KEY, IdUser int, Quantity int)
-- CREATE SYNONYM dbo.Usr FOR dbo.Users
-- and in SqlPrompterTest_Other: CREATE TABLE dbo.Archive (IdArchive int, Note nvarchar(20))
