using System.Collections.Concurrent;
using SmsOpsHQ.Core.DTOs;

namespace SmsOpsHQ.Desktop.Services;

// Client-side in-memory cache for threads, messages, and customers.
// Reduces redundant API calls when switching between views.
public sealed class CacheService
{
    private readonly ConcurrentDictionary<int, ThreadDto> _threads = new();
    private readonly ConcurrentDictionary<int, List<MessageDto>> _messagesByThread = new();
    private readonly ConcurrentDictionary<int, CustomerDto> _customers = new();

    // Threads

    public void SetThread(ThreadDto thread) => _threads[thread.ThreadId] = thread;

    public ThreadDto? GetThread(int threadId) =>
        _threads.TryGetValue(threadId, out ThreadDto? thread) ? thread : null;

    public void SetThreads(IEnumerable<ThreadDto> threads)
    {
        foreach (ThreadDto thread in threads)
            _threads[thread.ThreadId] = thread;
    }

    // Messages

    public void SetMessages(int threadId, List<MessageDto> messages) =>
        _messagesByThread[threadId] = messages;

    public List<MessageDto>? GetMessages(int threadId) =>
        _messagesByThread.TryGetValue(threadId, out List<MessageDto>? messages) ? messages : null;

    public void AppendMessage(int threadId, MessageDto message)
    {
        if (_messagesByThread.TryGetValue(threadId, out List<MessageDto>? messages))
        {
            messages.Add(message);
        }
    }

    // Customers

    public void SetCustomer(CustomerDto customer) => _customers[customer.CustomerId] = customer;

    public CustomerDto? GetCustomer(int customerId) =>
        _customers.TryGetValue(customerId, out CustomerDto? customer) ? customer : null;

    // Clear all caches.
    public void Clear()
    {
        _threads.Clear();
        _messagesByThread.Clear();
        _customers.Clear();
    }
}
