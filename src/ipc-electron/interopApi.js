class InteropApi {
    constructor() {
    	return new Proxy(this, {
    		get(target, prop) {
				if (WINDOWS) {
					return undefined;
				}
				// If the property is not a method of InteropApi, 
				// treat it as a .NET class name
				if (typeof prop === 'string' && !target[prop]) {
					return new Proxy({}, {
						get(_, methodName) {
							// Return a method that calls the .NET method dynamically
							return async (...args) => {
								return await target.callMethod(prop, methodName, ...args);
							};
						}
					});
				}
				return target[prop];
    		}
    	});
    }
  
    async callMethod(className, methodName, ...args) {
			if (WEB) {
				console.log('InteropApi.callMethod', className, methodName, args);
				const response = await fetch('/', {
					method: 'POST',
					headers: {
						'Content-Type': 'application/json'
					},
					body: JSON.stringify({
						className,
						methodName,
						args
					})
				});
				if (!response.ok) {
					throw new Error(`HTTP error! status: ${response.status}`);
				}
				const data = await response.json();
				if (data.status !== 'success') {
					throw new Error(`Error calling ${className}.${methodName}: ${data.error}`);
				}
				return data.result;
			}
    	return window.interopApi.callDotNetMethod(className, methodName, args)
    		.then(result => {
    			return result;
    	});
    }
}

export default new InteropApi();