-- Test database for INSERT column lists and EXEC parameters.
USE master;
GO
IF DB_ID('SqlPrompterTest_InsertExec') IS NOT NULL
BEGIN
    ALTER DATABASE SqlPrompterTest_InsertExec SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE SqlPrompterTest_InsertExec;
END
CREATE DATABASE SqlPrompterTest_InsertExec;
GO
USE SqlPrompterTest_InsertExec;
GO
CREATE TABLE dbo.Customers (IdCustomers INT NOT NULL IDENTITY PRIMARY KEY, Name NVARCHAR(100) NOT NULL, Email VARCHAR(200) NULL);
CREATE TABLE dbo.Orders
(
    IdOrders INT NOT NULL IDENTITY PRIMARY KEY,
    IdCustomers INT NOT NULL REFERENCES dbo.Customers (IdCustomers),
    OrderDate DATETIME NOT NULL DEFAULT GETDATE(),
    Quantity INT NOT NULL,
    Total DECIMAL(18, 2) NOT NULL,
    Note NVARCHAR(200) NULL,
    Price2 AS (Total * 2),
    RowVer ROWVERSION
);
GO
CREATE PROCEDURE dbo.AddOrder
    @IdCustomers INT,
    @Quantity INT = 1,
    @Note NVARCHAR(200) = NULL,
    @Label VARCHAR(20) = 'a, b',
    @IdOrders INT = 0 OUTPUT
AS
BEGIN
    INSERT INTO dbo.Orders (IdCustomers, Quantity, Total) VALUES (@IdCustomers, @Quantity, 0);
    SET @IdOrders = SCOPE_IDENTITY();
END
GO
CREATE PROCEDURE dbo.Cleanup AS DELETE FROM dbo.Orders WHERE Quantity = 0;
GO
CREATE SCHEMA sales;
GO
CREATE PROCEDURE sales.Report @Year INT, @Month INT AS SELECT @Year, @Month;
GO
