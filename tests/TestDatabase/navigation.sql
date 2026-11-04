-- Objects for checking F12 (go to definition): a table, a view, a procedure, a function and a trigger.
USE master;
GO
IF DB_ID('SqlPrompterTest_Nav') IS NOT NULL
BEGIN
    ALTER DATABASE SqlPrompterTest_Nav SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE SqlPrompterTest_Nav;
END
GO
CREATE DATABASE SqlPrompterTest_Nav;
GO
USE SqlPrompterTest_Nav;
GO
CREATE TABLE dbo.Customers (IdCustomers int IDENTITY(1,1) NOT NULL, Name nvarchar(100) NOT NULL, Note nvarchar(max) NULL, CONSTRAINT PK_Customers PRIMARY KEY (IdCustomers));
GO
CREATE VIEW dbo.vCustomers AS SELECT IdCustomers, Name FROM dbo.Customers;
GO
CREATE PROCEDURE dbo.usp_GetCustomers @Name nvarchar(100) AS
BEGIN
    SELECT * FROM dbo.Customers WHERE Name = @Name;
END
GO
CREATE FUNCTION dbo.fnCustomerCount() RETURNS int AS
BEGIN
    RETURN (SELECT COUNT(*) FROM dbo.Customers);
END
GO
CREATE TRIGGER dbo.trCustomersInsert ON dbo.Customers AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
END
GO
CREATE FUNCTION dbo.fnTax(@amount decimal(18,2), @rate decimal(5,2)) RETURNS decimal(18,2) AS
BEGIN
    RETURN @amount * @rate / 100;
END
GO
CREATE FUNCTION dbo.fnCustomersByName(@name nvarchar(100)) RETURNS TABLE AS
RETURN (SELECT IdCustomers, Name FROM dbo.Customers WHERE Name LIKE @name + '%');
GO
