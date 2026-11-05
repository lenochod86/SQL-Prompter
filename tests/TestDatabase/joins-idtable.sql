-- Test database for JOIN suggestions on a schema built like real ones: the key column is IdTable everywhere, an order references
-- addresses twice under role names, categories and employees reference themselves, Enrollments has a key over two columns.
-- The same schema is in tests/SqlPrompter.Tests/JoinRealisticSchemaTests.cs.
USE master;
GO
IF DB_ID('SqlPrompterTest_IdTable') IS NOT NULL
BEGIN
    ALTER DATABASE SqlPrompterTest_IdTable SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE SqlPrompterTest_IdTable;
END
CREATE DATABASE SqlPrompterTest_IdTable;
GO
USE SqlPrompterTest_IdTable;
GO
CREATE TABLE dbo.Customers (IdCustomer INT NOT NULL PRIMARY KEY, Name NVARCHAR(100) NOT NULL);
CREATE TABLE dbo.Addresses (IdAddress INT NOT NULL PRIMARY KEY, Street NVARCHAR(100) NOT NULL);
CREATE TABLE dbo.Orders (
    IdOrder INT NOT NULL PRIMARY KEY,
    IdCustomer INT NOT NULL CONSTRAINT FK_Orders_Customers REFERENCES dbo.Customers (IdCustomer),
    IdBillingAddress INT NOT NULL CONSTRAINT FK_Orders_BillingAddress REFERENCES dbo.Addresses (IdAddress),
    IdShippingAddress INT NOT NULL CONSTRAINT FK_Orders_ShippingAddress REFERENCES dbo.Addresses (IdAddress),
    Created DATETIME NOT NULL);
CREATE TABLE dbo.Categories (
    IdCategory INT NOT NULL PRIMARY KEY,
    IdParentCategory INT NULL CONSTRAINT FK_Categories_Parent REFERENCES dbo.Categories (IdCategory),
    Name NVARCHAR(100) NOT NULL);
CREATE TABLE dbo.Products (
    IdProduct INT NOT NULL PRIMARY KEY,
    IdCategory INT NOT NULL CONSTRAINT FK_Products_Categories REFERENCES dbo.Categories (IdCategory),
    Name NVARCHAR(100) NOT NULL);
CREATE TABLE dbo.OrderItems (
    IdOrderItem INT NOT NULL PRIMARY KEY,
    IdOrder INT NOT NULL CONSTRAINT FK_OrderItems_Orders REFERENCES dbo.Orders (IdOrder),
    IdProduct INT NOT NULL CONSTRAINT FK_OrderItems_Products REFERENCES dbo.Products (IdProduct),
    Quantity INT NOT NULL);
CREATE TABLE dbo.Employees (
    IdEmployee INT NOT NULL PRIMARY KEY,
    IdManager INT NULL CONSTRAINT FK_Employees_Manager REFERENCES dbo.Employees (IdEmployee),
    Name NVARCHAR(100) NOT NULL);
CREATE TABLE dbo.Students (IdStudent INT NOT NULL PRIMARY KEY, Name NVARCHAR(100) NOT NULL);
CREATE TABLE dbo.Courses (IdCourse INT NOT NULL PRIMARY KEY, Name NVARCHAR(100) NOT NULL);
CREATE TABLE dbo.Enrollments (
    IdStudent INT NOT NULL CONSTRAINT FK_Enrollments_Students REFERENCES dbo.Students (IdStudent),
    IdCourse INT NOT NULL CONSTRAINT FK_Enrollments_Courses REFERENCES dbo.Courses (IdCourse),
    Grade INT NULL,
    CONSTRAINT PK_Enrollments PRIMARY KEY (IdStudent, IdCourse));
-- No foreign keys: linked to the others only by the IdTable naming convention.
CREATE TABLE dbo.AuditLog (IdAudit INT NOT NULL PRIMARY KEY, IdCustomer INT NULL, IdOrder INT NULL, Message NVARCHAR(MAX) NULL);
GO
