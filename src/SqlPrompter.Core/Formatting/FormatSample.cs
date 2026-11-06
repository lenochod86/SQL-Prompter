namespace SqlPrompter.Core.Formatting
{
    /// <summary>Sample SQL (our own, made up) that the settings window formats with the style being edited, to show what each option does.</summary>
    public static class FormatSample
    {
        public const string Text =
            "select top 10 c.Name,c.City,count(*) as Orders from sales.Customers c inner join sales.Payments p on p.IdCustomer=c.IdCustomer and p.Paid=1 " +
            "where c.Active=1 and (c.City='Prague' or c.City='Brno') group by c.Name,c.City having count(*)>2 order by Orders desc;\r\n" +
            "select c.Name,case when c.Active=1 then 'yes' else 'no' end as IsActive from sales.Customers c where c.IdCustomer in (select o.IdCustomer from sales.Orders o where o.Total>1000);\r\n" +
            "declare @id int=5;\r\n" +
            "update dbo.Products set UnitPrice=UnitPrice*1.1,Modified=getdate() where IdProduct=@id;\r\n" +
            "insert into dbo.AuditLog(Action,Created) values('price',getdate());\r\n" +
            "if @id>0 begin select 1 end";
    }
}
