﻿#region Copyright 
// Copyright 2017 HS Inc.  All rights reserved.
// 
// Licensed under the Apache License, Version 2.0 (the "License"); 
// you may not use this file except in compliance with the License.  
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDER AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
// ARE DISCLAIMED.  IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE
// LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
// CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
// SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
// INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
// CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
// ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
// POSSIBILITY OF SUCH DAMAGE.
#endregion

using System;
using System.Threading.Tasks;
using HS.Microcore.ServiceDiscovery;
using HS.Microcore.ServiceDiscovery.Config;
using HS.Microcore.ServiceDiscovery.Rewrite;
using HS.Microcore.SharedLogic.Rewrite;

namespace HS.Microcore.Fakes.Discovery
{
    public class AlwaysLocalhostDiscovery : IDiscovery
    {
        private Func<DeploymentIdentifier, INodeSource, ReachabilityCheck, TrafficRoutingStrategy, ILoadBalancer> _createLoadBalancer {get;}

        public AlwaysLocalhostDiscovery(Func<DeploymentIdentifier, INodeSource, ReachabilityCheck, TrafficRoutingStrategy, ILoadBalancer> createLoadBalancer)
        {
            _createLoadBalancer = createLoadBalancer;
        }

        public ILoadBalancer CreateLoadBalancer(DeploymentIdentifier deploymentIdentifier, ReachabilityCheck reachabilityCheck, TrafficRoutingStrategy trafficRoutingStrategy)
        {
            return _createLoadBalancer(deploymentIdentifier, new LocalNodeSource(), reachabilityCheck, trafficRoutingStrategy);
        }

        public async Task<Node[]> GetNodes(DeploymentIdentifier deploymentIdentifier)
        {
            return new LocalNodeSource().GetNodes();
        }

        public void Dispose()
        {
        }
    }
}