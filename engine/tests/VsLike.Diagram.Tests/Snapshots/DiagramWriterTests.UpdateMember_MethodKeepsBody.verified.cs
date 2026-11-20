using System;

namespace Shop;

/// <summary>An order.</summary>
public class Order
{
    // keep this comment
    private readonly int _id;

    public Order(int id)
    {
        _id = id;
    }

    public string Customer { get; set; } = "";

    /// <summary>Total price.</summary>
    internal virtual decimal Total(bool withTax)
    {
        return 0m; // trailing comment
    }
}

public interface IOrderRepository
{
    Order? Find(int id);
}

public enum Status
{
    New,
    Paid
}