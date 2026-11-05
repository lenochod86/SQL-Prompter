-- Test database for the JOIN suggestions (customers, orders, order items with foreign keys).
USE master;
GO
IF DB_ID('SqlPrompterTest_Joins') IS NOT NULL
BEGIN
    ALTER DATABASE SqlPrompterTest_Joins SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE SqlPrompterTest_Joins;
END
CREATE DATABASE SqlPrompterTest_Joins;
GO
USE SqlPrompterTest_Joins;
GO
CREATE TABLE dbo.Customers (IdCustomers INT NOT NULL PRIMARY KEY, Name NVARCHAR(100) NOT NULL);
CREATE TABLE dbo.Orders (IdOrders INT NOT NULL PRIMARY KEY, IdCustomers INT NOT NULL REFERENCES dbo.Customers (IdCustomers), OrderDate DATE NOT NULL);
CREATE TABLE dbo.OrderItems (IdOrderItems INT NOT NULL PRIMARY KEY, IdOrders INT NOT NULL REFERENCES dbo.Orders (IdOrders), Quantity INT NOT NULL);
GO
