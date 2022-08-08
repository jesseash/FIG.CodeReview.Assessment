namespace FIG.Assessment;

/// <summary>
/// In this example, the goal of this GetPeopleInfo method is to fetch a list of Person IDs from our database (not implemented / not part of this example),
/// and for each Person ID we need to hit some external API for information about the person. Then we would like to return a dictionary of the results to the caller.
/// We want to perform this work in parallel to speed it up, but we don't want to hit the API too frequently, so we are trying to limit to 5 requests at a time at most
/// by using 5 background worker threads.
/// Feel free to suggest changes to any part of this example.
/// In addition to finding issues and/or ways of improving this method, what is a name for this sort of queueing pattern?
/// </summary>

/*

1)	We have to get these records in the database processed as soon as possible but there is a bottleneck the number of api calls being made.
If we make too many api calls at once we may go over the limit. We want 5 worker processes in the background to make the api calls while the 
queue is being populated by our own db.

2)	This looks like a producer consumer pattern of parallel programming using a concurrentqueue. MS has a built in concurrentqueue 
data structure which the documentation claims is thread safe
https://docs.microsoft.com/en-us/dotnet/api/system.collections.concurrent.concurrentqueue-1?view=net-6.0 
Here we are just using an ordinary queue datastucture I am noting here, which is probably not thread safe.


3)	We’re using threads instead of tasks, while task is an abstraction of a thread it does by default make use of a threadpool
which can make our program less expensive

4)	I see our logic waits for all the tasks/threads to complete but I don’t see it a addressing a scenario 
where the consumers (the background workers)  empty the queue quicker than the producer our db can fill it. 
This could cause the background threads to exit prematurely and our hashmap would only incomplete. So we need some sort of 
flag that keep the background processes from exiting until the queue is entirely populated

5)	Through my research I discovered that this is a fairly common scenario and the ms documentation recommends 
using this blocking collection https://docs.microsoft.com/en-us/dotnet/standard/collections/thread-safe/how-to-add-and-take-items

With the blocking collection there is a flag that can be set when the producer is finished if the flag has not 
been set and the queue is empty the consuming tasks will automatically pause until the queue is not empty

6)	Another advantage to using the blocking collection is that it is not necessary to make the size dynamic, 
because if the producer goes over it assigned size it blocks adding. Perhaps we want another method of tweaking 
how many api call are made per second besides increasing or decreasing the number of worker processes. If we wished 
to decrease the number of api calls we could make the size of blocking collection slower which would make it empty quicker
and cause the consumers to pause more often

7)	I would investigate the api calls themselves that were being made and see if perhaps there is bulk call that can be made. 
For example an api call that returns a list of all people and their age under some criteria which is the same as those people we have 
in our database
*/


public class Example1
{
    public Dictionary<int, int> GetPeopleInfo()
    {
        // initialize empty queue, and empty result set
        var personIdQueue = new Queue<int>();
        var results = new Dictionary<int, int>();

        // start thread which will populate the queue
        var collectThread = new Thread(() => CollectPersonIds(personIdQueue));
        collectThread.Start();

        // start 5 worker threads to read through the queue and fetch info on each item, adding to the result set
        var gatherThreads = new List<Thread>();
        for (var i = 0; i < 5; i++)
        {
            var gatherThread = new Thread(() => GatherInfo(personIdQueue, results));
            gatherThread.Start();
            gatherThreads.Add(gatherThread);
        }

        // wait for all threads to finish
        collectThread.Join();
        foreach (var gatherThread in gatherThreads)
            gatherThread.Join();

        return results;
    }

    private void CollectPersonIds(Queue<int> personIdQueue)
    {
        // dummy implementation, would be pulling from a database
        for (var i = 1; i < 100; i++)
        {
            if (i % 10 == 0) Thread.Sleep(TimeSpan.FromMilliseconds(50)); // artificial delay every now and then
            personIdQueue.Enqueue(i);
        }
    }

    private void GatherInfo(Queue<int> personIdQueue, Dictionary<int, int> results)
    {
        // pull IDs off the queue until it is empty
        while (personIdQueue.TryDequeue(out var id))
        {
            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://some.example.api/people/{id}/age");
            var response = client.SendAsync(request).Result;
            var age = int.Parse(response.Content.ReadAsStringAsync().Result);
            results[id] = age;
        }
    }
}
